using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AzDataMaker
{
    public static class RandomHelper
    {
        public static long NextLong(this Random rand, long min, long max)
        {
            byte[] buf = new byte[8];
            rand.NextBytes(buf);
            long longRand = BitConverter.ToInt64(buf, 0);

            return (Math.Abs(longRand % (max - min)) + min);
        }


    }
}
