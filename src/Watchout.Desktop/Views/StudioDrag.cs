using System.Windows;

namespace Watchout.Desktop.Views;

public static class StudioDrag
{
    public const string AssetFormat = "WatchMe.AssetId";
    public static string? AssetId { get; set; }

    public static bool TryAssetId(IDataObject data, out string id)
    {
        if (data.GetDataPresent(AssetFormat) && data.GetData(AssetFormat) is string custom && !string.IsNullOrWhiteSpace(custom))
        {
            id = custom;
            return true;
        }
        foreach (var format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.StringFormat })
        {
            if (data.GetDataPresent(format) && data.GetData(format) is string text && text.StartsWith("asset:", StringComparison.Ordinal))
            {
                id = text["asset:".Length..];
                return !string.IsNullOrWhiteSpace(id);
            }
        }
        if (!string.IsNullOrWhiteSpace(AssetId))
        {
            id = AssetId;
            return true;
        }
        id = "";
        return false;
    }

    public static string[] Files(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files ? files : [];

    public static bool IsMediaDrag(IDataObject data) => TryAssetId(data, out _) || Files(data).Length > 0;

    public static DataObject ForAsset(string assetId)
    {
        AssetId = assetId;
        var data = new DataObject();
        data.SetData(AssetFormat, assetId);
        data.SetData(DataFormats.UnicodeText, "asset:" + assetId);
        return data;
    }
}
