// Name: Difference ^ Negation Blend
// Submenu:
// Author: Nettakrim
// Title:
// Version: 1.1
// Desc: XORs the results of Difference blending and Negation blending on the selected layer and the one beneath it, and/or a constant color
// Keywords:
// URL: https://github.com/Nettakrim/PDN-Plugins
// Help: CC0 - Public Domain

#region UICode
LabelComment Label1 = "Blend with layer beneath selected"; // Main Section
DoubleSliderControl Mix = 1; // [-1,2] Mix, extrapolates when outside 0-1
IntSliderControl Op = 0; // [0,5] Operation [XOR, AND, OR, XNOR, NAND, NOR]
LabelComment Label2 = "\n"; // Constant Section
ColorWheelControl ConstColor = ColorWheelControl.Create(SrgbColors.Black); // Blend with a constant color
DoubleSliderControl ConstMix = 1; // [-1,2] Mix, extrapolates when outside 0-1
IntSliderControl ConstOp = 1; // [0,5] Operation [XOR, AND, OR, XNOR, NAND, NOR]
#endregion

private byte DNX(int a, int b, double mix, int op) {
    int diff = Math.Abs(b-a);
    int neg = 255 - Math.Abs(255-a-b);

    int combine = op switch {
        0 => diff ^ neg,
        1 => diff & neg,
        2 => diff | neg,
        3 => (diff ^ neg) ^ 255,
        4 => (diff & neg) ^ 255,
        5 => (diff | neg) ^ 255,
        _ => 0
    };

    return Lerp(a, combine, mix);
}

private byte Lerp(int a, int b, double mix) {
    return (byte)Math.Clamp(Math.Round((a + (b-(int)a)*mix)),0,255);
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

    int layers = Environment.Document.Layers.Count;
    int current = Environment.SourceLayerIndex;

    ColorBgra32 constant = ConstColor.GetBgra32(ConstColor.ColorContext);

    // cant blend if theres only one layer, so just DNX with constant
    if (layers <= 1) {
        for (int y = outputBounds.Top; y < outputBounds.Bottom; ++y)
        {
            if (IsCancelRequested) return;

            for (int x = outputBounds.Left; x < outputBounds.Right; ++x)
            {
                ColorBgra32 a = sourceRegion[x,y];
                for (int i = 0; i < 3; i++) {
                    a[i] = DNX(a[i], constant[i], ConstMix, ConstOp);
                }
                outputRegion[x,y] = a;
            }
        }
        return;
    }

    // get lower layer
    IBitmapEffectLayerInfo lowerInfo = Environment.Document.Layers[current == 0 ? 1 : current - 1];
    using IEffectInputBitmap<ColorBgra32> lowerBitmap = lowerInfo.GetBitmapBgra32();
    using IBitmapLock<ColorBgra32> lowerLock = lowerBitmap.Lock(new RectInt32(0, 0, lowerBitmap.Size));
        RegionPtr<ColorBgra32> lowerRegion = lowerLock.AsRegionPtr();

    // apply DNX filter
    for (int y = outputBounds.Top; y < outputBounds.Bottom; ++y)
    {
        if (IsCancelRequested) return;

        for (int x = outputBounds.Left; x < outputBounds.Right; ++x)
        {
            ColorBgra32 a = sourceRegion[x,y];
            ColorBgra32 b = lowerRegion[x,y];
            for (int i = 0; i < 3; i++) {
                a[i] = DNX(DNX(a[i],b[i], Mix, Op), constant[i], ConstMix, ConstOp);
            }
            outputRegion[x,y] = a;
        }
    }
}
