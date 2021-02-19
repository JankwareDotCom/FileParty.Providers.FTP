using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using FileParty.Core.Models;

namespace FileParty.Providers.FTP
{
    public enum ProxyType
    {
        HTTP1_1,
        USER_AT_HOST,
        BLUE_COAT
    }
    public class ProxyInfo : FluentFTP.Proxy.ProxyInfo
    {
        public ProxyType ProxyType { get; set; } = ProxyType.HTTP1_1;
    }
    
    public class FTPConfiguration : StorageProviderConfiguration<FTPModule>
    {
        public string BasePath { get; set; }
        public string Host { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public int Port { get; set; } = 900;
        public override char DirectorySeparationCharacter { get; } = '/';
        public ProxyInfo ProxyInfo { get; set; } = null;

        public FTPConfiguration()
        {
            
        }

        public FTPConfiguration(char directorySeparator)
        {
            DirectorySeparationCharacter = directorySeparator;
        }
    }

    public class FTPSConfiguration : FTPConfiguration
    {
        public SslProtocols SslProtocol { get; set; } = SslProtocols.None;
    }
    
    public class FTPX509Configuration : FTPSConfiguration
    {
        public List<X509Certificate2> Certificates { get; set; } = new List<X509Certificate2>();

        
        public FTPX509Configuration AddCert(X509Certificate2 cert)
        {
            Certificates.Add(cert);
            return this;
        }
    }
}