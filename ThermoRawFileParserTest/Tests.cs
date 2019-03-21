﻿using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;
using IO.Mgf;
using NUnit.Framework;
using ThermoRawFileParser;
using ThermoRawFileParser.Writer.MzML;

namespace ThermoRawFileParserTest
{
    
    [TestFixture]
    public class Tests
    {
        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        [Test]
        public void TestMgf()
        {
            // Get temp path for writing the test MGF
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"small.RAW");
            var parseInput = new ParseInput(testRawFile, tempFilePath, OutputFormat.Mgf, false, MetadataFormat.NON, false,
                "coll",
                "run", "sub", Log, null, null, null, null ,false);

            RawFileParser.Parse(parseInput);

            // Do this for the mzLib library issue
            var tempFileName = Path.GetTempPath() + "elements.dat";
            UsefulProteomicsDatabases.Loaders.LoadElements(tempFileName);

            var mgfData = Mgf.LoadAllStaticData(Path.Combine(tempFilePath, "small.mgf"));
            Assert.AreEqual(34, mgfData.NumSpectra);
            Assert.IsEmpty(mgfData.GetMS1Scans());
        }

        [Test]
        public void TestMzml()
        {
            // Get temp path for writing the test mzML
            var tempFilePath = Path.GetTempPath();

            var testRawFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"small.RAW");
            var parseInput = new ParseInput(testRawFile, tempFilePath, OutputFormat.Mzml, false, MetadataFormat.NON, false,
                "coll", "run", "sub", Log,  null, null, null, null, false);

            RawFileParser.Parse(parseInput);

            // Deserialize the mzML file
            var xmlSerializer = new XmlSerializer(typeof(mzMLType));
            var testMzMl = (mzMLType) xmlSerializer.Deserialize(new FileStream(
                Path.Combine(tempFilePath, "small.mzML"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

            Assert.AreEqual("48", testMzMl.run.spectrumList.count);
            Assert.AreEqual(48, testMzMl.run.spectrumList.spectrum.Length);

            Assert.AreEqual("1", testMzMl.run.chromatogramList.count);
            Assert.AreEqual(1, testMzMl.run.chromatogramList.chromatogram.Length);
        }
        
        [Test]
        public void TestHash()
        {
            byte[] myHash;
            using (var sha1 = new SHA1Managed())
            {
                using (var stream = File.OpenRead("/home/niels/Desktop/raw/test/small.pwiz.1.1_2.mzML"))
                    myHash = sha1.ComputeHash(stream);
                    
                    Console.WriteLine(BitConverter.ToString(myHash).Replace("-", "").ToLowerInvariant());
            }
        }
                
    }
}