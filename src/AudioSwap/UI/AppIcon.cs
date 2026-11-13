namespace AudioSwap.UI;

internal static class AppIcon
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("AudioSwap.ico")
            ?? throw new InvalidOperationException("AudioSwap.ico is not embedded.");
        return new Icon(stream, size);
    }

    public static Bitmap LoadBitmap(int size)
    {
        using var icon = Load(new Size(size, size));
        return icon.ToBitmap();
    }
}
