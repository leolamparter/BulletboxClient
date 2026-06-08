using System.Numerics;
using System.Text.Json.Serialization;

public struct SerializableVector2
{
    public float X { get; set; }
    public float Y { get; set; }

    public SerializableVector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    // Implicit conversion from Vector2 to SerializableVector2
    public static implicit operator SerializableVector2(Vector2 v) => new SerializableVector2(v.X, v.Y);

    // Implicit conversion from SerializableVector2 to Vector2
    public static implicit operator Vector2(SerializableVector2 sv) => new Vector2(sv.X, sv.Y);
}