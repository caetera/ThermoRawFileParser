################## BASE IMAGE ######################
FROM mono:latest

################## METADATA ######################
LABEL base_image="mono:latest"
LABEL version="1"
LABEL software="ThermoRawFileParser"
LABEL software.version="1.3.2"
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

WORKDIR /home/biodocker/
RUN mkdir -p /home/biodocker/build/ &&\
    git clone  -b master --single-branch --depth 1 https://github.com/caetera/ThermoRawFileParser /home/biodocker/build &&\
    msbuild /home/biodocker/build&&\
    mkdir /home/biodocker/TRFP &&\
    cp /home/biodocker/build/bin/x64/Debug/* /home/biodocker/TRFP

USER root
RUN cp /home/biodocker/build/ThermoRawFileParser /usr/local/bin/ &&\
    rm -rf /home/biodocker/build &&\
    ln -sr /usr/local/bin/ThermoRawFileParser /usr/local/bin/thermorawfileparser &&\
    ln -sr /usr/local/bin/ThermoRawFileParser /usr/local/bin/thermoparser &&\
    ln -sr /usr/local/bin/ThermoRawFileParser /usr/local/bin/ThermoParser &&\
    ln -sr /usr/local/bin/ThermoRawFileParser /usr/local/bin/trfp &&\
    chown biodocker:biodocker /usr/local/bin/* &&\
    chmod +x /usr/local/bin/ThermoRawFileParser

USER biodocker
