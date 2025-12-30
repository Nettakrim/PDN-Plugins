// Name: Even Histogram
// Submenu:
// Author: Nettakrim
// Title:
// Version:
// Desc: Evens out the histogram of the selected area
// Keywords:
// URL:
// Help: CC0 - Public Domain

// For help writing a GPU Image plugin: https://boltbait.com/pdn/CodeLab/help/tutorial/image/

#region UICode
IntSliderControl Seed = 0; // [0,1024] Seed for all the random decisions needed
IntSliderControl MaxIterations = 10000; // [0,1000000] Amount of iterations to try solve for, set to 0 to go until solved
CheckboxControl Quicker = true; // Massively speed up solving by smoothing more than just the global maximum. results may be less accurate
#endregion

public class Bar
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
}

public void Shift(Bar[] bars, int index, Random random) {
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

protected override void OnRender(IBitmapEffectOutput output)
{
    using IEffectInputBitmap<ColorBgra32> sourceBitmap = Environment.GetSourceBitmapBgra32();
    using IBitmapLock<ColorBgra32> sourceLock = sourceBitmap.Lock(new RectInt32(0, 0, sourceBitmap.Size));
        RegionPtr<ColorBgra32> sourceRegion = sourceLock.AsRegionPtr();

    RectInt32 outputBounds = output.Bounds;
    using IBitmapLock<ColorBgra32> outputLock = output.LockBgra32();
        RegionPtr<ColorBgra32> outputSubRegion = outputLock.AsRegionPtr();
    var outputRegion = outputSubRegion.OffsetView(-outputBounds.Location);
    //uint seed = RandomNumber.InitializeSeed(RandomNumberRenderSeed, outputBounds.Location);

    var selection = Environment.Selection.RenderBounds;

    Random random = new Random(Seed);

    // initialise histogram
    Bar[] bars = new Bar[256];
    for (int i = 0; i < 256; i++)
    {
        bars[i] = new Bar();
    }


    // set histogram values
    for (int y = selection.Top; y < selection.Bottom; ++y)
    {
        for (int x = selection.Left; x < selection.Right; ++x)
        {
            ColorBgra32 sourcePixel = sourceRegion[x, y];
            bars[sourcePixel.R].Increment(sourcePixel.R, 1);
        }
    }

    // iteratively diffuse histogram
    for (int x = 0; x < MaxIterations || MaxIterations == 0; x++)
    {
        if (IsCancelRequested) break;

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

        if (Quicker) {
            int shuffleA = random.Next(256);
            int shuffleB = random.Next(256);
            for (int i = 0; i < 256; i++)
            {
                Shift(bars, ((i ^ shuffleA)+shuffleB)&255, random);
            }
        }
    }

    // transpose histogram, so that each bar contains all the information needed for the weighted random
    Bar[] transposed = new Bar[256];
    for (int i = 0; i < 256; i++)
    {
        transposed[i] = new Bar();
    }

    for (int i = 0; i < 256; i++)
    {
        for (int j = 0; j < 256; j++)
        {
            transposed[i].Increment(j, bars[j].GetValue(i));
        }
    }

    #if DEBUG
    Debug.WriteLine("applying changes "+Random.Shared.Next());
    #endif

    // apply changes - for each pixel, set color to the index from a weighted random of all the histograms that have that value
    for (int y = selection.Top; y < selection.Bottom; ++y)
    {
        for (int x = selection.Left; x < selection.Right; ++x)
        {
            ColorBgra32 sourcePixel = sourceRegion[x, y];
            int index = sourcePixel.R;

            // set destination to weighted random
            byte newValue = (byte)(transposed[sourcePixel.R].ClaimValue(random));
            sourcePixel.R = newValue;
            sourcePixel.G = newValue;
            sourcePixel.B = newValue;
            try
            {
                outputRegion[x, y] = sourcePixel;
            }
            catch
            {

            }
        }
    }
}