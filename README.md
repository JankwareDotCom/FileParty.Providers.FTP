# FileParty.Providers.FTP

A FTP Provider for [FileParty](https://github.com/JankwareDotCome/FileParty) leveraging [FluentFTP](https://github.com/robinrodricks/FluentFTP)

![dotnet_test](https://github.com/JankwareDotCom/FileParty.Providers.FTP/workflows/dotnet_test/badge.svg)
[![Nuget Package](https://badgen.net/nuget/v/FileParty.Providers.FTP)](https://www.nuget.org/packages/FileParty.Providers.FTP/)

## How To Use

Register `FTPModule` as you would any other FileParty Module.  This Module has a few different configuration objects
- FTPConfiguration (for everyday ftp)
- FTPSConfiguration (for ftps with no certs)
- FTPX509Configuration (for ftps with x509 certs)

## Contributions / Issues / Requests Welcome
Check FileParty Core Repository for information
