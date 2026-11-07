using System.Runtime.InteropServices;
using Mnet.Core.Remoting;
using SkiaSharp;

namespace Mnet.App.Services;

public sealed class ScreenCapturer : IRemoteScreen
{
    private const int MaxFrameWidth = 1600;
    private const int JpegQuality = 72;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const uint DibRgbColors = 0;

    public ScreenFrame Capture()
    {
        var screenWidth = Math.Max(1, GetSystemMetrics(SmCxScreen));
        var screenHeight = Math.Max(1, GetSystemMetrics(SmCyScreen));

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            throw new InvalidOperationException("No se pudo acceder a la pantalla.");
        }

        var memoryDc = IntPtr.Zero;
        var bitmapHandle = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmapHandle = CreateCompatibleBitmap(screenDc, screenWidth, screenHeight);
            if (memoryDc == IntPtr.Zero || bitmapHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("No se pudo preparar la captura.");
            }

            previousObject = SelectObject(memoryDc, bitmapHandle);
            if (!BitBlt(memoryDc, 0, 0, screenWidth, screenHeight, screenDc, 0, 0, SrcCopy | CaptureBlt))
            {
                throw new InvalidOperationException("La captura falló; puede que la sesión esté bloqueada.");
            }

            using var bitmap = new SKBitmap(new SKImageInfo(
                screenWidth, screenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque));

            var header = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = screenWidth,
                    Height = -screenHeight,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };

            if (GetDIBits(memoryDc, bitmapHandle, 0, (uint)screenHeight, bitmap.GetPixels(), ref header, DibRgbColors) == 0)
            {
                throw new InvalidOperationException("No se pudo leer la imagen de la pantalla.");
            }

            var scale = Math.Min(1.0, MaxFrameWidth / (double)screenWidth);
            var frameWidth = Math.Max(1, (int)Math.Round(screenWidth * scale));
            var frameHeight = Math.Max(1, (int)Math.Round(screenHeight * scale));

            using var resized = bitmap.Resize(
                new SKImageInfo(frameWidth, frameHeight, SKColorType.Bgra8888, SKAlphaType.Opaque),
                SKFilterQuality.Low) ?? throw new InvalidOperationException("No se pudo escalar la captura.");
            using var encoded = resized.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
                ?? throw new InvalidOperationException("No se pudo comprimir la captura.");

            return new ScreenFrame(frameWidth, frameHeight, screenWidth, screenHeight, encoded.ToArray());
        }
        finally
        {
            if (previousObject != IntPtr.Zero)
            {
                SelectObject(memoryDc, previousObject);
            }

            if (bitmapHandle != IntPtr.Zero)
            {
                DeleteObject(bitmapHandle);
            }

            if (memoryDc != IntPtr.Zero)
            {
                DeleteDC(memoryDc);
            }

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ColorsUsed;
        public int ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, uint rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr deviceContext, IntPtr bitmap, uint startScanLine,
        uint scanLineCount, IntPtr bits, ref BitmapInfo info, uint usage);
}
