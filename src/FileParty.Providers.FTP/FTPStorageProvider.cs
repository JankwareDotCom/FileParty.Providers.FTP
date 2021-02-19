using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Threading;
using System.Threading.Tasks;
using FileParty.Core.Enums;
using FileParty.Core.EventArgs;
using FileParty.Core.Exceptions;
using FileParty.Core.Interfaces;
using FileParty.Core.Models;
using FluentFTP;
using FluentFTP.Proxy;

namespace FileParty.Providers.FTP
{
    public class FTPStorageProvider : IAsyncStorageProvider, IStorageProvider
    {
        public char DirectorySeparatorCharacter => _config.DirectorySeparationCharacter;
        private readonly StorageProviderConfiguration<FTPModule> _config;
        private string BasePath = string.Empty;

        public FTPStorageProvider(StorageProviderConfiguration<FTPModule> config)
        {
            _config = config;
        }
        
        public FtpClient GetClient()
        {
            if (!(_config is FTPConfiguration baseConfig))
            {
                throw new StorageException("FTP-001", "Invalid FTP Config");
            }

            BasePath = baseConfig.BasePath;
            
            FtpClient client;
            if (baseConfig.ProxyInfo != null)
            {
                client = baseConfig.ProxyInfo.ProxyType switch
                {
                    ProxyType.HTTP1_1 => new FtpClientHttp11Proxy(baseConfig.ProxyInfo),
                    ProxyType.USER_AT_HOST => new FtpClientUserAtHostProxy(baseConfig.ProxyInfo),
                    ProxyType.BLUE_COAT => new FtpClientBlueCoatProxy(baseConfig.ProxyInfo),
                    _ => new FtpClient()
                };
            }
            else
            {
                client = new FtpClient();
            }

            client.Host = baseConfig.Host;
            client.Credentials = new NetworkCredential(baseConfig.User, baseConfig.Password);
            client.Port = baseConfig.Port;
            
            if (_config is FTPSConfiguration ftps)
            {
                client.EncryptionMode = FtpEncryptionMode.Auto;
                client.SslProtocols = ftps.SslProtocol;
            }
            
            if (_config is FTPX509Configuration x509)
            {
                client.EncryptionMode = FtpEncryptionMode.Explicit;
                x509.Certificates.ForEach(c => client.ClientCertificates.Add(c));
                client.ValidateCertificate += (ClientOnValidateCertificate);
            }

            return client;
        }

        private static void ClientOnValidateCertificate(FtpClient control, FtpSslValidationEventArgs e)
        {
            e.Accept = e.PolicyErrors == SslPolicyErrors.None;
        }

        private string GetFullPath(string storagePointer)
        {
            var basePath = storagePointer.StartsWith(BasePath)
                ? string.Empty
                : BasePath;
            
            var dirSep =
                BasePath.EndsWith(DirectorySeparatorCharacter) && 
                !storagePointer.StartsWith(DirectorySeparatorCharacter)
                    ? string.Empty
                    : DirectorySeparatorCharacter.ToString();

            return $"{basePath}{dirSep}{storagePointer}";
        }
        
        public async Task<Stream> ReadAsync(string storagePointer, CancellationToken cancellationToken = new CancellationToken())
        {
            var type = await TryGetStoredItemTypeAsync(storagePointer, cancellationToken);
            
            if (type == null) throw Errors.FileNotFoundException;
            if (type != StoredItemType.File) throw Errors.MustBeFile;

            using var client = GetClient();
            await client.ConnectAsync(cancellationToken);

            var stream = await client.OpenReadAsync(GetFullPath(storagePointer), cancellationToken);
            return stream;
        }

        public async Task<bool> ExistsAsync(string storagePointer, CancellationToken cancellationToken = new CancellationToken())
        {
            var exists = await ExistsAsync(new[] {storagePointer}, cancellationToken);
            return exists.First().Value;
        }

        public async Task<IDictionary<string, bool>> ExistsAsync(IEnumerable<string> storagePointers, CancellationToken cancellationToken = new CancellationToken())
        {
            using var client = GetClient();
            await client.ConnectAsync(cancellationToken);
            var result = new Dictionary<string, bool>();
            
            foreach (var storagePointer in storagePointers)
            {
                result[storagePointer] =
                    await client.FileExistsAsync(GetFullPath(storagePointer), cancellationToken) ||
                    await client.DirectoryExistsAsync(GetFullPath(storagePointer), cancellationToken);
            }

            return result;
        }

        public async Task<StoredItemType?> TryGetStoredItemTypeAsync(string storagePointer, CancellationToken cancellationToken = new CancellationToken())
        {
            var info = await GetInformationAsync(storagePointer, cancellationToken);
            return info?.StoredType;
        }

        public async Task<IStoredItemInformation> GetInformationAsync(string storagePointer, CancellationToken cancellationToken = new CancellationToken())
        {
            using var client = GetClient();
            if (client == null) throw Errors.UnknownException;
            await client.ConnectAsync(cancellationToken);
            
            if (!await client.FileExistsAsync(GetFullPath(storagePointer), cancellationToken)) return null;
            
            var info = await client.GetObjectInfoAsync(GetFullPath(storagePointer), true, cancellationToken);
            var result = new StoredItemInformation
            {
                CreatedTimestamp = info?.Created,
                DirectoryPath = GetFullPath(storagePointer),
                LastModifiedTimestamp = info?.Modified,
                Name = info?.Name,
                Size = info?.Size,
                StoragePointer = storagePointer,
                StoredType = info?.Type == FtpFileSystemObjectType.Directory 
                    ? StoredItemType.Directory 
                    : StoredItemType.File
            };

            return result;
        }
        
        public async Task WriteAsync(FilePartyWriteRequest request, CancellationToken cancellationToken)
        {
            if (await ExistsAsync(request.StoragePointer, cancellationToken) && request.WriteMode == WriteMode.Create)
            {
                throw Errors.FileAlreadyExistsException;
            }
            
            using var client = GetClient();
            if (client == null)
            {
                throw Errors.UnknownException;
            }
            await client.ConnectAsync(cancellationToken);
            var reply = await client.UploadAsync(
                request.Stream, 
                GetFullPath(request.StoragePointer), 
                FtpRemoteExists.Overwrite, 
                true, 
                new Progress<FtpProgress>(a => 
                    WriteProgressEvent?.Invoke(
                        this, 
                        new WriteProgressEventArgs(
                            request.Id, 
                            request.StoragePointer, 
                            a.TransferredBytes, 
                            request.Stream.Length)))
                ,cancellationToken);
            
            if (reply != FtpStatus.Success)
            {
                throw new StorageException("FTP-002", "FTP Server did not indicate success");
            }
        }

        public async Task WriteAsync(string storagePointer, Stream stream, WriteMode writeMode, CancellationToken cancellationToken = new CancellationToken())
        {
            await WriteAsync(new FilePartyWriteRequest(storagePointer, stream, writeMode), cancellationToken);
        }

        public async Task DeleteAsync(string storagePointer, CancellationToken cancellationToken = new CancellationToken())
        {
            await DeleteAsync(new[] {storagePointer}, cancellationToken);
        }

        public async Task DeleteAsync(IEnumerable<string> storagePointers, CancellationToken cancellationToken = new CancellationToken())
        {
            using var client = GetClient();
            if (client == null) throw Errors.UnknownException;
            await client.ConnectAsync(cancellationToken);
            
            foreach (var storagePointer in storagePointers)
            {
                if(await client.DirectoryExistsAsync(GetFullPath(storagePointer),cancellationToken))
                {
                    await client.DeleteDirectoryAsync(GetFullPath(storagePointer), cancellationToken);
                } 
                else if (await client.FileExistsAsync(GetFullPath(storagePointer), cancellationToken))
                {
                    await client.DeleteFileAsync(GetFullPath(storagePointer), cancellationToken);
                }
            }
        }

        public void Write(FilePartyWriteRequest request)
        {
            WriteAsync(request, CancellationToken.None).Wait();
        }

        public void Write(string storagePointer, Stream stream, WriteMode writeMode)
        {
            Write(new FilePartyWriteRequest(storagePointer, stream, writeMode));
        }

        public void Delete(string storagePointer)
        {
            Delete(new []{storagePointer});
        }

        public void Delete(IEnumerable<string> storagePointers)
        {
            DeleteAsync(storagePointers, CancellationToken.None).Wait();
        }

        public event EventHandler<WriteProgressEventArgs> WriteProgressEvent;

        public Stream Read(string storagePointer)
        {
            return ReadAsync(storagePointer).Result;
        }

        public bool Exists(string storagePointer)
        {
            return Exists(new[] {storagePointer}).First().Value;
        }

        public IDictionary<string, bool> Exists(IEnumerable<string> storagePointers)
        {
            return ExistsAsync(storagePointers).Result;
        }

        public bool TryGetStoredItemType(string storagePointer, out StoredItemType? type)
        {
            type = TryGetStoredItemTypeAsync(storagePointer, CancellationToken.None).Result;
            return type != null;
        }

        public IStoredItemInformation GetInformation(string storagePointer)
        {
            return GetInformationAsync(storagePointer, CancellationToken.None).Result;
        }
    }
}