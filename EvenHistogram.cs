// Name: Even Histogram
// Submenu:
// Author: Nettakrim
// Title:
// Version: 1.4
// Desc: Evens out the histogram of the selected area
// Keywords:
// URL: https://github.com/Nettakrim/PDN-Plugins
// Help: CC0 - Public Domain, when building make sure to enable Single Threaded and Single Render Call

#region UICode
DoubleSliderControl Mix = 1; // [-1,2] Mix, extrapolates when outside 0-1
CheckboxControl Grayscale = false; // Grayscale, using red channel
CheckboxControl Denoising = false; // Enable denoising, will mean histogram isnt perfectly even
DoubleSliderControl Median = 1; // [0,1] {Denoising} Denoising Strength
CheckboxControl RunAgain = false; // {Denoising} Run again after denoising\n(Makes histogram even again, but can reintroduce noise)
IntSliderControl Seed = 0; // [0,65535] Seed for all the random decisions needed
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

    private void AverageSide(int start, int end, int total) {
        int steps = Math.Abs(start-end);

        if (steps == 0) {
            values[end] += total;
            return;
        }

        // get slope needed to spread out values in a triangle
        // this is slightly different from the usual equation for triangular numbers, since it weights the center half (since it gets double covered by each side)
        double slope = (2*total)/(steps*(double)steps);
        for (int i = 1; i < steps; i++) {
            int index = start < end ? start + (i - 1) : start - (i - 1);

            int rounded = (int)Math.Round(i * slope);
            total -= rounded;
            values[index] = rounded;
        }

        // in most cases, there should be counts left over, but because of the rounding, this might not be true
        if (total >= 0) {
            values[end] += total;
            return;
        }

        // in which case the missing total needs to be removed from the end of the triangle
        for (int i = 1; i < steps; i++) {
            int index = start < end ? start + (i - 1) : start - (i - 1);

            total += values[index];

            if (total >= 0) {
                values[index] = total;
                break;
            }

            values[index] = 0;
        }
    }


    public void Average(double quartile) {
        if (amount == 0) {
            return;
        }

        // get quartile thresholds
        int lowerAmount = (int)Math.Round(amount * quartile/2);
        int middleAmount = (int)Math.Round(amount / 2.0);
        int upperAmount = (int)Math.Round(amount * (1 - quartile/2));
        int half = middleAmount;

        // find quartile indices
        int lowerIndex = -1;
        int middleIndex = -1;
        int upperIndex = -1;

        for (int i = 0; i < 256; i++) {
            int value = values[i];

            lowerAmount -= value;
            middleAmount -= value;
            upperAmount -= value;

            if (lowerAmount < 0 && lowerIndex == -1) {
                lowerIndex = i;
            }
            if (middleAmount < 0 && middleIndex == -1) {
                middleIndex = i;
            }
            if (upperAmount <= 0 && upperIndex == -1) {
                upperIndex = i;
            }

            values[i] = 0;
        }

        // apply averaging to each side
        AverageSide(lowerIndex, middleIndex, half);
        AverageSide(Math.Max(upperIndex, middleIndex), middleIndex, amount - half);
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

    if (Denoising && average) {
        for (int i = 0; i < 256; i++) {
            transposed[i].Average(Median);
        }
    }

    return transposed;
}

private Bar[] GetBars(RectInt32 selection, ColorBgr24[,] pixels, int index) {
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

private void Run(RectInt32 selection, ColorBgr24[,] pixels, Random random, bool average) {
    if (Grayscale) {
        // colors are ordered bgra, so r is index 2
        Bar[] bars = Solve(GetBars(selection, pixels, 2), random, average);

        for (int y = 0; y < selection.Height; ++y)
        {
            if (IsCancelRequested) break;
            for (int x = 0; x < selection.Width; ++x)
            {
                ColorBgr24 sourcePixel = pixels[x, y];
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
                ColorBgr24 sourcePixel = pixels[x, y];
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

    ColorBgr24[,] pixels = new ColorBgr24[selection.Width, selection.Height];
    for (int y = 0; y < selection.Height; ++y)
    {
        if (IsCancelRequested) break;
        for (int x = 0; x < selection.Width; ++x)
        {
            ColorBgra32 color = sourceRegion[x + selection.Left, y + selection.Top];
            pixels[x,y] = new ColorBgr24(color.B, color.G, color.R);
        }
    }

    Run(selection, pixels, random, true);

    if (Denoising && RunAgain) {
        Run(selection, pixels, random, false);
    }

    for (int y = 0; y < selection.Height; ++y)
    {
        if (IsCancelRequested) break;
        for (int x = 0; x < selection.Width; ++x)
        {
            ColorBgr24 color = pixels[x, y];
            ColorBgra32 source = sourceRegion[x + selection.Left, y + selection.Top];
            outputRegion[x + selection.Left, y + selection.Top] = new ColorBgra32(Lerp(source.B, color.B), Lerp(source.G, color.G), Lerp(source.R, color.R), source.A);
        }
    }
}