using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Buttplug.Client; 

namespace Butthurt.Contracts.Services;

public class ButthurtService
{
    private static readonly ButtplugClient myClient = new("Butthurt - Game plugin manager");


    public static EventHandler<DeviceAddedEventArgs>? DeviceAdded;
    public static EventHandler<DeviceRemovedEventArgs>? DeviceRemoved;

    public static bool IsConnected => myClient.Connected;
    
    public static async Task Start()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;


        //var uri = new Uri("ws://localhost:12345");
        var connector = new ButtplugWebsocketConnector(new Uri("ws://localhost:12345"));


        var connectClient = myClient.ConnectAsync(connector);


        try
        {
            await connectClient;
        }
        catch (ButtplugClientConnectorException e)
        {
            var innerException = e.InnerException;
            Console.WriteLine($"ButtplugConnector error: {innerException?.Message.Replace(" More detailed information in inner exception.", string.Empty)}", Color.Red);

            if (innerException?.InnerException?.Message != null)
            {
                Console.WriteLine($"Details: {innerException?.InnerException.Message}", Color.Red);
            }

            Console.WriteLine("Try manually configuring URI for Intiface Central instead", Color.Red);
        }
        catch (Exception e)
        {
            Console.WriteLine($"ButtplugConnector error: {e}", Color.Red);
        }

        myClient.DeviceAdded += (obj, addedArgs) => {
            DeviceAdded?.Invoke(obj, addedArgs);
        };

        myClient.DeviceRemoved += (obj, removeArgs) => {
            DeviceRemoved?.Invoke(obj, removeArgs);
        };


        
        Console.WriteLine("Butthurt Service connected.",Color.Green);
    }
    
    public static async Task<List<string>?> ScanForDevices()
    {
        if (!myClient.Connected)
        {
            Console.Write("You need to start Butthurt service first to be able to scan for devices!");
            return null;
        }


        await myClient.StartScanningAsync();
        await Task.Delay(5000); // Wait for a few seconds to allow devices to be discovered
        await myClient.StopScanningAsync();

        if (myClient.Devices.Length <= 0)
        {
            return null;
        }

        return myClient.Devices.Select(x => x.Name).ToList();
    }

    public static async Task Stop()
    {
        if (!myClient.Connected)
        {
            Console.Write("You need to start Butthurt service first to be able to stop it!");
            return;
        }

        await myClient.DisconnectAsync(); ;
    }
    
    private static void OnProcessExit(object sender, EventArgs e)
    {
        try
        {
            // Run cleanup work in the background, don't block Avalonia dispatcher
            Task.Run(async () =>
            {
                try
                {
                    await Stop();
                }
                catch (TaskCanceledException)
                {
                    // Expected during shutdown, no problem
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Exception during async cleanup: {ex}");
                }
            }).Wait(2000); // optional: wait up to 2s for graceful shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception on exit: {ex}");
        }
        
        Console.WriteLine("Butthurt service stopped", Color.Green);
    }

    public static List<string> GetDevices()
    {
        if (!myClient.Connected)
        {
            return new List<string>();
        }
        
        return myClient.Devices.Select(x => x.Name).ToList();
    }
}