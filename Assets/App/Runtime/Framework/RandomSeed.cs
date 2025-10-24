namespace App.Runtime.Framework
{
    // xor shift algorithm
    public class RandomSeed
    {
        private int _seed = 12345;

        public void Initialize(int seed)
        {
            _seed = seed;
        }
        
        public int Next()
        {
            _seed ^= (_seed << 13);
            _seed ^= (_seed >> 17);
            _seed ^= (_seed << 5);
            return _seed & int.MaxValue;
        }
    }
}