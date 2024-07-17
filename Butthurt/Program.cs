using Buttplug.Client;
using Buttplug.Client.Connectors.WebsocketConnector;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

class Program
{
    
    // CLEAN UP LATEER MAYBE
    
    private static ButtplugClient client;

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, ref Rect rect);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    static async Task Main(string[] args)
    {
        Console.WriteLine("Press ESC to stop");
        client = new ButtplugClient("WoW Haptic Feedback Client");
        
        var connector = new ButtplugWebsocketConnector(new Uri("ws://localhost:12345/buttplug"));
        await client.ConnectAsync(connector);
//
        
        // Start scanning for devices
       await client.StartScanningAsync();
       await Task.Delay(5000); // Wait for a few seconds to allow devices to be discovered
       await client.StopScanningAsync();

//        // Check if any devices are connected
        if (client.Devices.Length > 0)
        {
            Console.WriteLine("Haptic device found.");
            
            client.ScanningFinished += (a, b) =>
            {
                Console.WriteLine("Scanning finished.");
            };
        }
        else
        {
            Console.WriteLine("No haptic devices found.");
            return;
        }

        while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
        {
            IntPtr hWnd = GetForegroundWindow();
            Rect rect = new Rect();
            GetWindowRect(hWnd, ref rect);

            // Capture the top left pixel
            Bitmap bitmap = CaptureScreen(rect.Left, rect.Top, 1, 1);
            Color pixelColor = GetPixelColor(bitmap, 0, 0);

            if (IsColorRed(pixelColor))
            {
                OnRedPixelDetected();
            }

            // Dispose the bitmap
            bitmap.Dispose();

            // Sleep for a bit to reduce CPU usage
            Thread.Sleep(100);
        }
    }

    static Bitmap CaptureScreen(int x, int y, int width, int height)
    {
        Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }

    static Color GetPixelColor(Bitmap bitmap, int x, int y)
    {
        return bitmap.GetPixel(x, y);
    }

    static bool IsColorRed(Color color)
    {
        return color.R > 200 && color.G < 50 && color.B < 50;
    }

    static async Task OnRedPixelDetected()
    {
        
        foreach (var device in client.Devices)
        {
            Console.WriteLine($"{device.Name} supports vibration: ${device.VibrateAttributes.Count > 0}");
            if (device.VibrateAttributes.Count > 0)
            {
                Console.WriteLine($" - Number of Vibrators: {device.VibrateAttributes.Count}");
            }
        }

        try
        {
            var testClientDevice = client.Devices[0];
            Console.WriteLine($"Starting vibration for device: {testClientDevice.Name}");
            StartVibration(testClientDevice).Wait();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
           
        }
        
    }
    
    static async Task StartVibration(ButtplugClientDevice device)
    {
        Console.WriteLine($"Starting vibration for device: {device.Name}");
        await device.VibrateAsync(1.0f);
        await Task.Delay(500); // Duration of the vibration
        await device.VibrateAsync(0);
    }
}


