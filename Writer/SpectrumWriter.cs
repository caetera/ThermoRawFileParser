﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using log4net;
using ThermoFisher.CommonCore.Data;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.Data.FilterEnums;
using ThermoFisher.CommonCore.Data.Interfaces;
using ThermoRawFileParser.Util;
using System.Linq;

namespace ThermoRawFileParser.Writer
{
    public abstract class SpectrumWriter : ISpectrumWriter
    {
        private static readonly ILog Log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        protected const double ZeroDelta = 0.0001;

        /// <summary>
        /// The progress step size in percentage.
        /// </summary>
        protected const int ProgressPercentageStep = 10;

        private const double PrecursorMzDelta = 0.0001;
        private const double DefaultIsolationWindowLowerOffset = 1.5;
        private const double DefaultIsolationWindowUpperOffset = 2.5;

        /// <summary>
        /// The parse input object
        /// </summary>
        protected readonly ParseInput ParseInput;

        /// <summary>
        /// The output stream writer
        /// </summary>
        protected StreamWriter Writer;

        /// <summary>
        /// Precursor cache
        /// </summary>
        private static LimitedSizeDictionary<int, MZArray> precursorCache;

        // Precursor scan number (value) and isolation m/z (key) for reference in the precursor element of an MSn spectrum
        private protected readonly Dictionary<string, int> _precursorScanNumbers = new Dictionary<string, int>();

        //Precursor information for scans
        private protected Dictionary<int, PrecursorInfo> _precursorTree = new Dictionary<int, PrecursorInfo>();

        // Filter string regex to extract an isoaltion entry
        private protected readonly Regex _filterStringIsolationMzPattern = new Regex(@"ms\d+ (.+?) \[");

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="parseInput">the parse input object</param>
        protected SpectrumWriter(ParseInput parseInput)
        {
            ParseInput = parseInput;
            precursorCache = new LimitedSizeDictionary<int, MZArray>(10);
            _precursorScanNumbers[""] = -1;
            _precursorTree[-1] = new PrecursorInfo();
        }

        /// <inheritdoc />
        public abstract void Write(IRawDataPlus rawFile, int firstScanNumber, int lastScanNumber);

        /// <summary>
        /// Configure the output writer
        /// </summary>
        /// <param name="extension">The extension of the output file</param>
        protected void ConfigureWriter(string extension)
        {
            if (ParseInput.StdOut)
            {
                Writer = new StreamWriter(Console.OpenStandardOutput());
                Writer.AutoFlush = true;
                return;
            }

            var fileName = NormalizeFileName(ParseInput.OutputFile, extension, ParseInput.Gzip);
            if (ParseInput.OutputFormat == OutputFormat.Parquet)
            {
                Writer = new StreamWriter(File.Create(fileName));
            }
            else if (!ParseInput.Gzip || ParseInput.OutputFormat == OutputFormat.IndexMzML)
            {
                Writer = File.CreateText(fileName);
            }
            else
            {
                var fileStream = File.Create(fileName);
                var compress = new GZipStream(fileStream, CompressionMode.Compress);
                Writer = new StreamWriter(compress);
            }
            
        }

        private string NormalizeFileName(string outputFile, string extension, bool gzip)
        {
            string result = outputFile == null ? Path.Combine(ParseInput.OutputDirectory, ParseInput.RawFileNameWithoutExtension) : outputFile;
            string tail = "";

            string[] extensions;
            if (gzip)
                extensions = new string[] { ".gz", extension };
            else
                extensions = new string[] { extension };

            result = result.TrimEnd('.');

            foreach (var ext in extensions)
            {    
                if (result.ToLower().EndsWith(ext.ToLower()))
                    result = result.Substring(0, result.Length - ext.Length);

                tail = ext + tail;
                result = result.TrimEnd('.');
            }

            return result + tail;
        }

        /// <summary>
        /// Construct the spectrum title.
        /// </summary>
        /// <param name="scanNumber">the spectrum scan number</param>
        protected static string ConstructSpectrumTitle(int instrumentType, int instrumentNumber, int scanNumber)
        {
            return $"controllerType={instrumentType} controllerNumber={instrumentNumber} scan={scanNumber}";
        }

        /// <summary>
        /// Calculate the selected ion m/z value. This is necessary because the precursor mass found in the reaction
        /// isn't always the monoisotopic mass.
        /// https://github.com/ProteoWizard/pwiz/blob/master/pwiz/data/vendor_readers/Thermo/SpectrumList_Thermo.cpp#L564-L574
        /// </summary>
        /// <param name="reaction">the scan event reaction</param>
        /// <param name="monoisotopicMz">the monoisotopic m/z value</param>
        /// <param name="isolationWidth">the scan event reaction</param>
        public static double CalculateSelectedIonMz(IReaction reaction, double? monoisotopicMz,
            double? isolationWidth)
        {
            var selectedIonMz = reaction.PrecursorMass;

            // take the isolation width from the reaction if no value was found in the trailer data
            if (isolationWidth == null || isolationWidth < ZeroDelta)
            {
                isolationWidth = reaction.IsolationWidth;
            }

            isolationWidth /= 2;

            if (monoisotopicMz != null && monoisotopicMz > ZeroDelta
                                       && Math.Abs(
                                           reaction.PrecursorMass - monoisotopicMz.Value) >
                                       PrecursorMzDelta)
            {
                selectedIonMz = monoisotopicMz.Value;

                // check if the monoisotopic mass lies in the precursor mass isolation window
                // otherwise take the precursor mass                                    
                if (isolationWidth <= 2.0)
                {
                    if ((selectedIonMz <
                         (reaction.PrecursorMass - DefaultIsolationWindowLowerOffset * 2)) ||
                        (selectedIonMz >
                         (reaction.PrecursorMass + DefaultIsolationWindowUpperOffset)))
                    {
                        selectedIonMz = reaction.PrecursorMass;
                    }
                }
                else if ((selectedIonMz < (reaction.PrecursorMass - isolationWidth)) ||
                         (selectedIonMz > (reaction.PrecursorMass + isolationWidth)))
                {
                    selectedIonMz = reaction.PrecursorMass;
                }
            }

            return selectedIonMz;
        }

        public static IReaction GetReaction(IScanEvent scanEvent, int scanNumber)
        {
            IReaction reaction = null;
            try
            {
                var order = (int) scanEvent.MSOrder;
                reaction = scanEvent.GetReaction(order - 2);
            }
            catch (ArgumentOutOfRangeException)
            {
                Log.Warn("No reaction found for scan " + scanNumber);
            }

            return reaction;
        }

        /// <summary>
        /// Calculate the precursor peak intensity (similar to modern MSConvert).
        /// Sum intensities of all peaks in the isolation window.
        /// </summary>
        /// <param name="rawFile">the RAW file object</param>
        /// <param name="precursorScanNumber">the precursor scan number</param>
        /// <param name="precursorMass">the precursor mass</param>
        /// <param name="isolationWidth">the isolation width</param>
        /// <param name="useProfile">profile/centroid switch</param>
        protected static double? CalculatePrecursorPeakIntensity(IRawDataPlus rawFile, int precursorScanNumber,
            double precursorMass, double? isolationWidth, bool useProfile)
        {
            double precursorIntensity = 0;
            double halfWidth = isolationWidth is null || isolationWidth == 0 ? 0 : DefaultIsolationWindowLowerOffset; // that is how it is made in MSConvert (why?)

            double[] masses;
            double[] intensities;

            // Get the mz-array from RAW file or cache
            if (precursorCache.ContainsKey(precursorScanNumber))
            {
                masses = precursorCache[precursorScanNumber].Masses;
                intensities = precursorCache[precursorScanNumber].Intensities;
            }
            else
            {
                Scan scan = Scan.FromFile(rawFile, precursorScanNumber);

                if (useProfile) //get the profile data
                {
                    masses = scan.SegmentedScan.Positions;
                    intensities = scan.SegmentedScan.Intensities;
                }
                else
                {
                    if (scan.HasCentroidStream) //use centroids if possible
                    {
                        masses = scan.CentroidScan.Masses;
                        intensities = scan.CentroidScan.Intensities;
                    }
                    else
                    {
                        var scanEvent = rawFile.GetScanEventForScanNumber(precursorScanNumber);
                        var centroidedScan = scanEvent.ScanData == ScanDataType.Profile //only centroid profile spectra
                            ? Scan.ToCentroid(scan).SegmentedScan
                            : scan.SegmentedScan;

                        masses = centroidedScan.Positions;
                        intensities = centroidedScan.Intensities;
                    }
                }

                //save to cache
                precursorCache.Add(precursorScanNumber, new MZArray { Masses = masses, Intensities = intensities });
            }

            var index = masses.FastBinarySearch(precursorMass - halfWidth); //set index to the first peak inside isolation window

            while (index > 0 && index < masses.Length && masses[index] < precursorMass + halfWidth) //negative index means value was not found
            {
                precursorIntensity += intensities[index];
                index++;
            }

            return precursorIntensity;
        }

        private protected int GetParentFromScanString(string scanString)
        {
            var parts = Regex.Split(scanString, " ");

            //find the position of the first (from the end) precursor with a different mass 
            //to account for possible supplementary activations written in the filter
            var lastIonMass = parts.Last().Split('@').First();
            int last = parts.Length;
            while (last > 0 &&
                   parts[last - 1].Split('@').First() == lastIonMass)
            {
                last--;
            }

            string parentFilter = String.Join(" ", parts.Take(last));
            if (_precursorScanNumbers.ContainsKey(parentFilter))
            {
                return _precursorScanNumbers[parentFilter];
            }

            return -2; //unsuccessful parsing
        }

        private protected int FindLastReaction(IScanEvent scanEvent, int msLevel)
        {
            int lastReactionIndex = msLevel - 2;

            //iteratively trying find the last available index for reaction
            while (true)
            {
                try
                {
                    scanEvent.GetReaction(lastReactionIndex + 1);
                }
                catch (ArgumentOutOfRangeException)
                {
                    //stop trying
                    break;
                }

                lastReactionIndex++;
            }

            //supplemental activation flag is on -> one of the levels (not necissirily the last one) used supplemental activation
            //check last two activations
            if (scanEvent.SupplementalActivation == TriState.On)
            {
                var lastActivation = scanEvent.GetReaction(lastReactionIndex).ActivationType;
                var beforeLastActivation = scanEvent.GetReaction(lastReactionIndex - 1).ActivationType;

                if ((beforeLastActivation == ActivationType.ElectronTransferDissociation || beforeLastActivation == ActivationType.ElectronCaptureDissociation) &&
                    (lastActivation == ActivationType.CollisionInducedDissociation || lastActivation == ActivationType.HigherEnergyCollisionalDissociation))
                    return lastReactionIndex - 1; //ETD or ECD followed by HCD or CID -> supplemental activation in the last level (move the last reaction one step back)
                else
                    return lastReactionIndex;
            }
            else //just use the last one
            {
                return lastReactionIndex;
            }
        }
    }
}