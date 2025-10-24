namespace App.Runtime.Framework
{
    public static class RandomExtension
    {
        public static float ToFloat01(this int value, int min, int max)
        {
            return (float)(value - min) / (float)(max - min);
        }

        public static float ToFloat01(this int value)
        {
            return value.ToFloat01(0, int.MaxValue);
        }
    }
}