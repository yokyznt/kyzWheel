namespace KyzWheel.Convert;

public static class FileGuards
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public static string Ext(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static void EnsureLocal(string path)
    {
        if (!File.Exists(path)) throw new ConversionException("El archivo ya no existe");
        var a = File.GetAttributes(path);
        if ((a & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0)
            throw new ConversionException("Archivo solo en la nube: ábrelo una vez para descargarlo");
    }
}
