using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MowerUpdater;

internal class MsiHelper
{
    const int MaxGuidLength = 39;  // GUID 的最大长度
    const int MaxProductNameLength = 512;  // 产品名称的最大长度

    [DllImport("msi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int MsiEnumProductsW(int iProductIndex, StringBuilder lpProductBuf);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    static extern int MsiGetProductInfo(string product, string property, StringBuilder valueBuf, ref int len);

    public static bool CheckIfInstalled(string program)
    {
        var productID = new StringBuilder(MaxGuidLength);
        var productName = new StringBuilder(MaxProductNameLength);

        for (int i = 0; ; i++)
        {
            if (MsiEnumProductsW(i, productID) != 0)
                break;

            int length = MaxProductNameLength;
            if (MsiGetProductInfo(productID.ToString(), "ProductName", productName, ref length) == 0)
            {
                if (productName.ToString().IndexOf(program, StringComparison.OrdinalIgnoreCase) != -1)
                {
                    return true;
                }
            }
        }
        return false;
    }
}
