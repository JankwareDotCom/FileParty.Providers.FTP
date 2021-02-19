using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileParty.Core.Enums;
using FileParty.Core.Exceptions;
using FileParty.Core.Interfaces;
using FileParty.Core.Models;
using FileParty.Core.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace FileParty.Providers.FTP.Tests
{
    public class FTPProviderShould
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public FTPProviderShould(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        
        [Fact]
        public async Task DoAllTheThingsOnce()
        {
            var sc = this.AddFileParty(c => c.AddModule(new FTPConfiguration('/')
            {
                BasePath = "",
                Host = Environment.GetEnvironmentVariable("FP_FTP_HOST"),
                User = Environment.GetEnvironmentVariable("FP_FTP_USER"),
                Password = Environment.GetEnvironmentVariable("FP_FTP_PASS"),
                Port = int.Parse(Environment.GetEnvironmentVariable("FP_FTP_PORT") ?? "900"),
                ProxyInfo = null
            }));
            
            await using var sp = sc.BuildServiceProvider();
            var ftp = sp.GetRequiredService<IAsyncStorageProvider>();
            var sub = sp.GetRequiredService<IWriteProgressSubscriptionManager>();
            sub.SubscribeToAll((a, b) => _testOutputHelper.WriteLine($"{b.StoragePointer} - {b.PercentComplete}"));

            // cheat and clear all contents
            await ftp.DeleteAsync("myfile", CancellationToken.None);
            
            // check if file exists, it doesn't
            var storagePointer = $"myfile{ftp.DirectorySeparatorCharacter}thing.txt";
            Assert.False(await ftp.ExistsAsync(storagePointer));
            
            // create file
            await using var inputStream = new MemoryStream();
            await using var inputWriter = new StreamWriter(inputStream);
            await inputWriter.WriteAsync(new string('*', 12 * 1024)); // 12kb string
            await inputWriter.FlushAsync();
            inputStream.Position = 0;

            var request = new FilePartyWriteRequest(storagePointer, inputStream, WriteMode.Create);
            await ftp.WriteAsync(request, CancellationToken.None);

            // check if file exists, it does
            Assert.True(await ftp.ExistsAsync(storagePointer));

            // get file info
            var info = await ftp.GetInformationAsync(storagePointer, CancellationToken.None);
            Assert.NotNull(info);
            Assert.Equal(StoredItemType.File, info.StoredType);

            // try to overwrite file, but fail
            await using var inputStream2 = new MemoryStream();
            await using var inputWriter2 = new StreamWriter(inputStream2);
            await inputWriter2.WriteAsync(new string('/', 12 * 1024)); // 12kb string
            await inputWriter2.FlushAsync();
            inputStream.Position = 0;
            request.Stream = inputStream2;
            await Assert.ThrowsAsync<StorageException>(async () =>
            {
                await ftp.WriteAsync(request, CancellationToken.None);
            });

            // try to overwrite file, but succeed
            await using var inputStream3 = new MemoryStream();
            await using var inputWriter3 = new StreamWriter(inputStream3);
            await inputWriter3.WriteAsync(new string('-', 12 * 1024)); // 12kb string
            await inputWriter3.FlushAsync();
            inputStream.Position = 0;
            request.Stream = inputStream3;
            request.WriteMode = WriteMode.CreateOrReplace;

            await ftp.WriteAsync(request, CancellationToken.None);
            await using var contents = await ftp.ReadAsync(storagePointer, CancellationToken.None);
            using var reader = new StreamReader(contents);
            var fileString = await reader.ReadToEndAsync();
            Assert.True(fileString.All(a=>a == '-'));

            // try to delete file
            await ftp.DeleteAsync(storagePointer, CancellationToken.None);
            Assert.False(await ftp.ExistsAsync(storagePointer, CancellationToken.None));
        }
    }
}
