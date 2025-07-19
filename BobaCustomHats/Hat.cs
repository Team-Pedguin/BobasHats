namespace BobaCustomHats;

public struct Hat
{
    public string Name;
    public GameObject Prefab;
    public Texture2D Icon;

    public Hat(string name, GameObject prefab, Texture2D icon)
    {
        Name = name;
        Prefab = prefab;
        Icon = icon;
    }
}