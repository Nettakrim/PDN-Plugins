// Name: Even Histogram
// Submenu:
// Author: Nettakrim
// Title:
// Version: 1.2
// Desc: Evens out the histogram of the selected area
// Keywords:
// URL: https://netal.co.uk/
// Help: CC0 - Public Domain, when building make sure to enable Single Threaded and Single Render Call

#region UICode
DoubleSliderControl Mix = 1; // [-1,2] Mix, values outside of 0-1 will extrapolate
DoubleSliderControl Median = 0; // [0,1] Choose the median value for each color, to remove noise
CheckboxControl RunAgain = false; // Run twice, works well with averaging on images that have gradients
CheckboxControl Grayscale = false; // Grayscale, using red channel
IntSliderControl Seed = 0; // [0,1024] Seed for all the random decisions needed
#endregion

private class Bar
{
    private int[] values;
    private int amount;

    public Bar()
    {
        values = new int[256];
        amount = 0;
    }

    public void Increment(int value, int count)
    {
        values[value] += count;
        amount += count;
    }

    public int GetAmount()
    {
        return amount;
    }

    public int GetValue(int index)
    {
        return values[index];
    }

    public void MoveCountTo(Bar other, int destination, int amount, Random random)
    {
        // update cached amounts
        this.amount -= amount;
        other.amount += amount;

        // find index of a count nearest to destination
        int index = destination;
        int delta = 0;
        int direction = random.Next() % 2;

        while (amount > 0)
        {
            if (index >= 0 && index <= 255) {
                int value = Math.Min(values[index], amount);
                amount -= value;

                // move count
                values[index] -= value;
                other.values[index] += value;
            }

            // bounce index back and forth so it goes 0, -1, 1, -2, 2, -3, 3 etc
            // eventually this will find a value > 0, since this is a mode
            delta++;
            // the value of direction being 0 or 1 means itll pick a random direction to favour each time
            if ((delta % 2) == direction)
            {
                index += delta;
            }
            else
            {
                index -= delta;
            }
        }
    }

    public int ClaimValue(Random random)
    {
        // get index from weighted random, used to apply the final values with the transposed histogram
        int choice = random.Next(amount);
        amount--;

        for (int i = 0; i < 256; i++)
        {
            choice -= values[i];
            if (choice < 0)
            {
                values[i]--;
                return i;
            }
        }

        return 0;
    }

    public void Average(double mix) {
        if (amount == 0) {
            return;
        }

        int remaining = amount;
        int total = amount/2;
        int average = -1;
        for (int i = 0; i < 256; i++) {
            total -= values[i];
            if (total < 0 && average == -1) {
                average = i;
                values[i] = 0;
            }
            else {
                values[i] = (int)(values[i] * mix);
                remaining -= values[i];
            }
        }

        values[average] = remaining;
    }
}


private void Shift(Bar[] bars, int index, Random random) {
    // get difference needed for each side
    int amount = bars[index].GetAmount();
    int lowerDelta = index == 0 ? 0 : Math.Max(0, amount - bars[index - 1].GetAmount());
    int upperDelta = index == 255 ? 0 : Math.Max(0, amount - bars[index + 1].GetAmount());

    // dont do anything if neither side is lower
    int total = lowerDelta + upperDelta;
    if (total == 0) {
        return;
    }

    // weighted random to favour moving to the more unbalanced side
    int destination;
    int shift;
    if (random.Next(total) < lowerDelta)
    {
        // move a count from maxIndex to maxIndex - 1
        destination = index - 1;
        shift = (lowerDelta+1)/2;
    }
    else
    {
        // move a count from maxIndex to maxIndex + 1
        destination = index + 1;
        shift = (upperDelta+1)/2;
    }

    bars[index].MoveCountTo(bars[destination], destination, shift, random);
}

private void Diffuse(Bar[] bars, Random random) {
    // iteratively diffuse histogram
    while (true)
    {
        if (IsCancelRequested) return;

        // get min and max
        int maxAmount = int.MinValue;
        int maxIndex = 0;
        int minAmount = int.MaxValue;

        for (int i = 0; i < 256; i++)
        {
            int amount = bars[i].GetAmount();
            if (amount < minAmount)
            {
                minAmount = amount;
            }

            if (amount > maxAmount)
            {
                // only count as a new max if its got at least one lower neighbor
                if ((i > 0 && bars[i - 1].GetAmount() < amount) || (i < 255 && bars[i + 1].GetAmount() < amount))
                {
                    maxAmount = amount;
                    maxIndex = i;
                }
            }
        }

        // stop if overall delta is 1, or if it couldnt find a maximum (since that means the histogram is fully resolved)
        if (maxAmount - minAmount <= 1 || maxAmount == int.MinValue) break;

        Shift(bars, maxIndex, random);

        int shuffleA = random.Next(256);
        int shuffleB = random.Next(256);
        for (int i = 0; i < 256; i++)
        {
            Shift(bars, ((i ^ shuffleA)+shuffleB)&255, random);
        }
    }
}

private Bar[] Transpose(Bar[] bars) {
    // transpose histogram, so that each bar contains all the information needed for the weighted random
    Bar[] transposed = new Bar[256];

    for (int i = 0; i < 256; i++)
    {
        transposed[i] = new Bar();
        for (int j = 0; j < 256; j++)
        {
            transposed[i].Increment(j, bars[j].GetValue(i));
        }
    }

    return transposed;
}

private Bar[] Solve(Bar[] bars, Random random, bool average) {
    Diffuse(bars, random);
    Bar[] transposed = Transpose(bars);

    if (Median > 0 && average) {
        for (int i = 0; i < 256; i++) {
            transposed[i].Average(1.0 - Median);
        }
    }

    return transposed;
}

private Bar[] GetBars(RectInt32 selection, ColorBgr32[,] pixels, int index) {
    // initialise histogram
    Bar[] bars = new Bar[256];
    for (int i = 0; i < 256; i++)
    {
        bars[i] = new Bar();
    }

    // set histogram values
    for (int y = 0; y < selection.Height; ++y)
    {
        if (IsCancelRequested) break;
        for (int x = 0; x < selection.Width; ++x)
        {
            int value = pixels[x, y][index];
            bars[value].Increment(value, 1);
        }
    }

    return bars;
}

private void Run(RectInt32 selection, ColorBgr32[,] pixels, Random random, bool average) {
    if (Grayscale) {
        // colors are ordered bgra, so r is index 2
        Bar[] bars = Solve(GetBars(selection, pixels, 2), random, average);

        for (int y = 0; y < selection.Height; ++y)
        {
            if (IsCancelRequested) break;
            for (int x = 0; x < selection.Width; ++x)
            {
                ColorBgr32 sourcePixel = pixels[x, y];
                byte newValue = (byte)(bars[sourcePixel[2]].ClaimValue(random));
                sourcePixel.R = newValue;
                sourcePixel.G = newValue;
                sourcePixel.B = newValue;

                pixels[x, y] = sourcePixel;
            }
        }
    }
    else
    {
        Bar[] r = Solve(GetBars(selection, pixels, 2), random, average);
        Bar[] g = Solve(GetBars(selection, pixels, 1), random, average);
        Bar[] b = Solve(GetBars(selection, pixels, 0), random, average);

        for (int y = 0; y < selection.Height; ++y)
        {
            if (IsCancelRequested) break;
            for (int x = 0; x < selection.Width; ++x)
            {
                ColorBgr32 sourcePixel = pixels[x, y];
                sourcePixel.R = (byte)(r[sourcePixel.R].ClaimValue(random));
                sourcePixel.G = (byte)(g[sourcePixel.G].ClaimValue(random));
                sourcePixel.B = (byte)(b[sourcePixel.B].ClaimValue(random));

                pixels[x, y] = sourcePixel;
            }
        }
    }
}

private byte Lerp(byte a, byte b) {
    return (byte)Math.Clamp(Math.Round((a + (b-(int)a)*Mix)),0,255);
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

    var selection = Environment.Selection.RenderBounds;

    Random random = new Random(Seed);

    ColorBgr32[,] pixels = new ColorBgr32[selection.Width, selection.Height];
    for (int y = 0; y < selection.Height; ++y)
    {
        if (IsCancelRequested) break;
        for (int x = 0; x < selection.Width; ++x)
        {
            pixels[x,y] = (ColorBgr32)sourceRegion[x + selection.Left, y + selection.Top];
        }
    }

    Run(selection, pixels, random, true);

    if (RunAgain) {
        Run(selection, pixels, random, false);
    }

    for (int y = 0; y < selection.Height; ++y)
    {
        if (IsCancelRequested) break;
        for (int x = 0; x < selection.Width; ++x)
        {
            ColorBgr32 color = pixels[x, y];
            ColorBgra32 source = sourceRegion[x + selection.Left, y + selection.Top];
            outputRegion[x + selection.Left, y + selection.Top] = new ColorBgra32(Lerp(source.B, color.B), Lerp(source.G, color.G), Lerp(source.R, color.R), source.A);
        }
    }
}