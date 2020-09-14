################## BASE IMAGE ######################
FROM mono:latest

################## METADATA ######################
LABEL base_image="mono:latest"
LABEL version="1"
LABEL software="ThermoRawFileParser"
LABEL software.version="1.3.1"
LABEL about.summary="A software to convert Thermo RAW files to mgf and mzML"
LABEL about.home="https://github.com/compomics/ThermoRawFileParser"
LABEL about.documentation="https://github.com/compomics/ThermoRawFileParser"
LABEL about.license_file="https://github.com/compomics/ThermoRawFileParser"
LABEL about.license="SPDX:Unknown"
LABEL about.tags="Proteomics"

################## MAINTAINER ######################
MAINTAINER Niels Hulstaert <niels.hulstaert@ugent.be>
MAINTAINER Yasset Perez-Riverol <ypriverol@gmail.com>

################## INSTALLATION ######################

USER root
RUN apt-get update
RUN apt-get install -y git

### Because we are not using the based image from biocontainers we need to create the user and data folder ########

RUN mkdir /data /config

RUN groupadd fuse && \
    useradd --create-home --shell /bin/bash --user-group --uid 1000 --groups sudo,fuse biodocker && \
    echo `echo "biodocker\nbiodocker\n" | passwd biodocker` && \
    chown biodocker:biodocker /data && \
    chown biodocker:biodocker /config


############## USER Create ##############

USER biodocker

RUN mkdir -p /home/biodocker/bin/
WORKDIR /home/biodocker/bin/
RUN git clone  -b master --single-branch --depth 1 https://github.com/compomics/ThermoRawFileParser /home/biodocker/bin
RUN msbuild
RUN mkdir /home/biodocker/TRFP &&\
    cp /home/biodocker/bin/bin/x64/Debug/* /home/biodocker/TRFP

USER root
RUN cp /home/biodocker/bin/ThermoRawFileParser /usr/local/bin/ &&\
    rm -rf /home/biodocker/bin 

RUN chown biodocker:biodocker /usr/local/bin/ThermoRawFileParser &&\
    chmod +x /usr/local/bin/ThermoRawFileParser
