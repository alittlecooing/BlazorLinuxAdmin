internal static class BLA_StaticExts
{
    //fix a bug for raspbian
    public static int SafeIndexOf (this string self, string part, int pos)
    {
        char fc = part[0];
        while (pos < self.Length)
        {
            pos = self.IndexOf(fc, pos);
            if (pos == -1)
            {
                return -1;
            }

            if (string.Compare(self, pos, part, 0, part.Length, false) == 0)
            {
                return pos;
            }

            pos++;
        }
        return -1;
    }
}
