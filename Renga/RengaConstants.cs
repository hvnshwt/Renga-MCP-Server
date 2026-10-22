namespace RengaMcpServer.Renga;

public static class RengaIds
{
    public static readonly Guid Level = new("c3ce17ff-6f28-411f-b18d-74fe957b2ba8");
    public static readonly Guid Axis = new("4b41ccf8-c969-4c55-a1f2-cced9c164f07");
    public static readonly Guid Wall = new("4329112a-6b65-48d9-9da8-abf1f8f36327");
    public static readonly Guid Column = new("d9ee2442-e807-42fb-8fe5-9dcfe543035d");
    public static readonly Guid Beam = new("63478188-7c88-4a6d-b891-9725f04a5bc7");
    public static readonly Guid Floor = new("f5bd8bd8-39c1-47f8-8499-f673c580dfbe");
    public static readonly Guid Roof = new("bac4470f-d560-4f57-a49e-faa5f6e5a279");
    public static readonly Guid Door = new("1cfba99c-01e7-4078-ae1a-3e2ff0673599");
    public static readonly Guid Window = new("2b02b353-2ca5-4566-88bb-917ea8460174");
    public static readonly Guid Line3D = new("02bbebe8-e28b-4ee5-8916-11b514a35dca");
}

public static class RengaParams
{
    public static readonly Guid LevelElevation = new("440a20f8-42b8-4a5f-9000-39ef58e0302b");
    public static readonly Guid LevelName = new("1bb1addf-a3c0-4356-9525-107ea7df1513");
    public static readonly Guid WallHeight = new("0c6c933c-e47c-40d2-ba84-b8ae5ccec6f1");
    public static readonly Guid WallThickness = new("25548335-7030-43b1-b602-9898f3adc3b0");
    public static readonly Guid ColumnHeight = new("4f10003e-b0e4-4074-bd8e-84610c2fedec");
    public static readonly Guid FloorThickness = new("f2712442-b9df-44fe-ac7b-c3524342c804");
    public static readonly Guid GridLineName = new("da64f3a9-4bb1-4ff8-bd50-706a2bdb81ad");
    public static readonly Guid GridLineExtension = new("01fd69be-1916-4070-b581-adf2bbd62963");
    public static readonly Guid ObjectName = new("a4bc7f50-463e-48f6-b829-795d4e0a1160");
    public static readonly Guid Offset = new("337fd89b-763e-46dc-b1a9-7aecb253adbf");
    public static readonly Guid DoorWidth = new("569911cd-d708-4274-bc17-107c6a5d47a1");
    public static readonly Guid DoorHeight = new("eae12886-6635-4292-b46e-e1a15b5df263");
    public static readonly Guid WindowWidth = new("bb780525-6156-4a7c-8010-0fd88ced8979");
    public static readonly Guid WindowHeight = new("c30a59c6-8bc7-4e33-befa-cd932735f84b");
    public static readonly Guid RoofThickness = new("b3bf56bc-b9c0-4554-93c2-67e7362468af");
}

public static class Units
{
    public const double MillimetersPerMeter = 1000.0;

    public static double ToMillimeters(double meters) => meters * MillimetersPerMeter;

    public static double ToMeters(double millimeters) => millimeters / MillimetersPerMeter;
}
