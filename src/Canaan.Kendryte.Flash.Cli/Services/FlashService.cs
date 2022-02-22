using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Canaan.Kendryte.Flash.Shell.Services;
using Microsoft.Extensions.Logging;

namespace Canaan.Kendryte.Flash.Cli.Services
{
    internal class FlashService
    {
        private readonly Options _options;
        private readonly uint _chip = 3;
        private readonly ProgressIndicator _progressIndicator;

        public FlashService(Options options, ProgressIndicator progressIndicator)
        {
            _options = options;
            _progressIndicator = progressIndicator;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return StartFlash();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StartFlash()
        {
            if (string.IsNullOrEmpty(_options.Firmware))
                throw new InvalidOperationException("Must specify firmware path.");
            var firmwareType = GetFirmwareType(_options.Firmware);
            if (firmwareType == FirmwareType.Unknown)
                throw new InvalidOperationException("Unknown firmware type.");
            if (string.IsNullOrEmpty(_options.Device))
                throw new InvalidOperationException("Must select device.");
            if (_options.BaudRate < 110)
                throw new InvalidOperationException("Invalid baud rate.");
            var start = DateTime.Now;

            using (var loader = new KendryteLoader(_options.Device, _options.BaudRate))
            {
                loader.CurrentJobChanged += (s, e) =>
                {
                    _progressIndicator.SetJobItem(loader.CurrentJob, loader.JobItemsStatus[loader.CurrentJob]);
                };

                void print(string msg)
                {
                    Console.WriteLine($"\r\n{msg}\r\n");
                }

                Console.WriteLine("Flashing...");

                //print("Booting to ISP mode");
                //await loader.BootToISPModeForBoardMaixGoOpenec();
                //print("Greeting");
                //await loader.Greeting();
                print("Detecting board");
                await loader.DetectBoardAsync();

                if (true)
                {
                    //await loader.ChangeBaudRate();
                    //await loader.InitializeFlash(_chip);
                    using (var file = File.OpenRead(_options.Firmware))
                    using (var br = new BinaryReader(file))
                    {
                        await loader.InstallFlashBootloader(br.ReadBytes((int)file.Length));
                    }
                }
                else
                {
                    print("Installing bootloader");
                    await loader.InstallFlashBootloader();
                    print("Booting bootloader");
                    await loader.BootBootloader();
                    print("Flash greeting");
                    await loader.FlashGreeting();
                    print("Change baud");
                    await loader.ChangeBaudRate();
                    print("Init flash");
                    await loader.InitializeFlash(_chip);

                    if (firmwareType == FirmwareType.Single)
                    {
                        using (var file = File.OpenRead(_options.Firmware))
                        using (var br = new BinaryReader(file))
                        {
                            print("Writing firmware");
                            await loader.FlashFirmware(0, br.ReadBytes((int)file.Length), true, false);
                        }
                    }
                    else if (firmwareType == FirmwareType.FlashList)
                    {
                        using (var pkg = new FlashPackage(File.OpenRead(_options.Firmware)))
                        {
                            await pkg.LoadAsync();

                            foreach (var item in pkg.Files)
                            {
                                using (var br = new BinaryReader(item.Bin))
                                {
                                    print("Writing firmware");
                                    await loader.FlashFirmware(item.Address, br.ReadBytes((int)item.Length), item.SHA256Prefix, item.Reverse4Bytes);
                                }
                            }
                        }
                    }
                }

                

                //await loader.Reboot();

                
            }

            Console.WriteLine(Environment.NewLine + $"Flash completed in {DateTime.Now - start}!");

            var t = new TerminalService();
            t.Start(_options.Device, 115200, _chip);

            
        }

        private FirmwareType GetFirmwareType(string firmware)
        {
            var ext = Path.GetExtension(firmware).ToLowerInvariant();

            switch (ext)
            {
                case ".bin":
                    return FirmwareType.Single;
                case ".kfpkg":
                    return FirmwareType.FlashList;
                default:
                    return FirmwareType.Unknown;
            }
        }

        private enum FirmwareType
        {
            Single,
            FlashList,
            Unknown
        }
    }
}
