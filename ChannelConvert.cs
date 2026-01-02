// Name: Channel Convert
// Submenu:
// Author: Nettakrim
// Title:
// Version: 1.0
// Desc: Convert from RGB to HSV, and swizzle channels
// Keywords:
// URL: https://github.com/Nettakrim/PDN-Plugins
// Help: CC0 - Public Domain

#region UICode
DoubleSliderControl Mix = 1; // [-1,2] Mix, extrapolates when outside 0-1
CheckboxControl InputHSV = false; // Input HSV
CheckboxControl OutputHSV = false; // Output HSV
TextboxControl Swizzle = "xyz"; // Output Swizzle, you can use rgba,hsv,xyzw,0-9 interchangeably
#endregion

private byte Lerp(float a, float b) {
    return (byte)Math.Clamp(Math.Round((a + (b-a)*Mix)),0,255);
}

protected override void OnRender(IBitmapEffectOutput output)
{
    using IEffectInputBitmap<ColorBgra32> sourceBitmap = Environment.GetSourceBitmapBgra32();
    using IBitmapLock<ColorBgra32> sourceLock = sourceBitmap.Lock(new RectInt32(0, 0, sourceBitmap.Size));
        RegionPtr<ColorBgra32> sourceRegion = sourceLock.AsRegionPtr();

    RectInt32 outputBounds = output.Bounds;
    using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
        RegionPtr<ColorBgra32> outputSubRegion = outputLock.AsRegionPtr();
    var outputRegion = outputSubRegion.OffsetView(-outputBounds.Location);

    int[] swizzle = new int[4] {0,1,2,3};
    string s = Swizzle.ToString().ToLowerInvariant();
    for (int i = 0; i < 4 && i < s.Length; i++) {
        swizzle[i] = s[i] switch {
            // standard swizzles
            'x' or 'r' or 'h' => 0,
            'y' or 'g' or 's' => 1,
            'z' or 'b' or 'v' => 2,
            'w' or 'a' => 3,
            // splash colors https://www.todepond.com/lab/splash/
            '0' => 4, '1' => 5, '2' => 5, '3' => 6, '4' => 7,
            '5' => 8, '6' => 9, '7' => 10, '8' => 11, '9' => 12,
            // fallback to identity swizzle
            _ => swizzle[i]
        };
    }

    float[] values = new float[4];
    byte[] result = new byte[4];

    for (int y = outputBounds.Top; y < outputBounds.Bottom; ++y)
    {
        if (IsCancelRequested) return;

        for (int x = outputBounds.Left; x < outputBounds.Right; ++x)
        {
            ColorBgra32 source = sourceRegion[x,y];
            values[3] = source.A;

            if (InputHSV == OutputHSV) {
                // color space doesnt need changing
                values[0] = source.R;
                values[1] = source.G;
                values[2] = source.B;
            }
            else if (OutputHSV) {
                // RGB -> HSV
                ColorHsv96Float convert = ColorHsv96Float.FromRgb(new ColorRgb96Float(source.R/255f, source.G/255f, source.B/255f));
                values[0] = convert.Hue * (255f / 360f);
                values[1] = convert.Saturation * (255f / 100f);
                values[2] = convert.Value * (255f / 100f);
            } else {
                // HSV -> RGB
                ColorRgb96Float convert = ColorRgb96Float.FromHsv(new ColorHsv96Float(source.R * (360f/255f), source.G * (100f/255f), source.B * (100f/255f)));
                values[0] = convert.R * 255f;
                values[1] = convert.G * 255f;
                values[2] = convert.B * 255f;
            }

            // apply swizzle
            for (int i = 0; i < 4; i++) {
                int v = swizzle[i];
                result[i] = Lerp(source[i == 3 ? 3 : 2-i], v >= 4 ? (v - 4)*(255f/8f) : values[v]);
            }

            outputRegion[x,y] = new ColorBgra32(result[2], result[1], result[0], result[3]);
        }
    }
    return;

}
