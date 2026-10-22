using Renga;

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
    public static readonly Guid Material = new("0abcb18f-0aaf-4509-bf89-5c5fad9d5d8b");
    public static readonly Guid LayeredMaterial = new("0f0adba0-5c06-46c0-9c8a-b9d69ef1251f");
    public static readonly Guid DoorStyle = new("19d0649f-582a-488e-a52b-585c1151a5e4");
    public static readonly Guid WindowStyle = new("fac43446-031c-413e-9993-6e9cf9f2306a");
    public static readonly Guid BeamStyle = new("cf2b8b04-f595-4432-98f4-8234c95adbdd");
    public static readonly Guid ColumnStyle = new("be49a354-19b7-435a-8957-9ef8782630c2");
    public static readonly Guid PlateStyle = new("9b60d6ad-3468-478e-94df-a535c5aeaa3e");
    public static readonly Guid ElementStyle = new("514a3ae7-f551-4d0f-b5ba-5d4f0ecf4e7a");
    public static readonly Guid Profile = new("8734b5cd-57fc-409e-aefe-1fdc449bcb5c");
    public static readonly Guid Opening = new("fc443d5a-b76c-45e5-b91c-520ef0896109");
    public static readonly Guid Plate = new("62cf086e-5a39-4484-840c-ffa6a1c6e2b7");
    public static readonly Guid Room = new("f1a805ff-573d-f46b-ffba-57f4bccaa6ed");
    public static readonly Guid Stair = new("3f522f49-aee2-4d73-9866-9b07cf336a69");
    public static readonly Guid Railing = new("a1aca786-78a4-4015-b412-9150baad71a9");
    public static readonly Guid Ramp = new("debde004-afcc-4da8-8dd0-4223ff836acd");
    public static readonly Guid WallFoundation = new("d7dd0293-dd65-4229-a64c-8b528d4e226f");
    public static readonly Guid IsolatedFoundation = new("6063816c-89ff-4c8f-a814-3be6cb94128e");
    public static readonly Guid Element = new("e1e3bd66-2e13-4fa4-a9eb-677e03067c2f");
    public static readonly Guid IfcLink = new("9bf0d3c4-4ca7-45e1-8ea3-dfbc675cedea");
    public static readonly Guid IfcModel = new("6485ac11-5b26-4d77-9788-7936af87c85f");
    public static readonly Guid ModelLink = new("d769d1c4-8c32-40a8-a716-68bc9b6b5d3c");
    public static readonly Guid Line3D = new("02bbebe8-e28b-4ee5-8916-11b514a35dca");
    public static readonly Guid Drawing = new("a7dfe1e1-bf2c-4c4a-ba74-3f156b1bbf8f");
    public static readonly Guid DrawingLine = new("681cabaf-4469-438e-83d7-1efd664eda6b");
    public static readonly Guid DrawingText = new("688cce66-411f-44a2-a5cc-149bdde3169c");
    public static readonly Guid DrawingImage = new("478b4ee8-a87c-4560-ba64-0b33791eddbf");
    public static readonly Guid LinearDimension = new("dc82ca1a-a0c3-4a1a-aefb-a7d720dd3a09");
    public static readonly Guid Tag = new("b5ff6d07-84a7-432f-be96-73f5b95d54f3");
    public static readonly Guid Elevation = new("8a49a9a8-a401-4ab1-8038-92093503c97a");
    public static readonly Guid Section = new("4166fd59-64c0-45ee-ae3b-49fae1257ef1");
    public static readonly Guid Table = new("ed1f87a1-5c9c-4994-969d-6d3854571193");
    public static readonly Guid Schedule = new("5cbf0016-32bc-4630-99ea-c7cc94dda8e3");
    public static readonly Guid Pipe = new("838cc9f6-e3d8-4132-af6f-c58df0f8d037");
    public static readonly Guid Duct = new("06cc88ee-9a67-4626-9c34-dde03c331a74");
    public static readonly Guid ElectricalCircuitLine = new("83de45e6-4793-49ec-8b9e-65a2438f36de");
    public static readonly Guid PipeFitting = new("d31dc2e3-808e-4987-8481-7f86665a07fc");
    public static readonly Guid PipeAccessory = new("41e2788a-49ed-487f-9ae1-55b6e09ae6e5");
    public static readonly Guid DuctFitting = new("77ffca60-b20e-49f0-b42f-4fdc9b1c825b");
    public static readonly Guid DuctAccessory = new("47d0d93f-3c7b-4269-bf8a-de246e1724d0");
    public static readonly Guid Route = new("8b323bee-3882-4744-8838-24f45df714a9");
    public static readonly Guid RoutePoint = new("ce93e320-7167-4cd1-92a8-5e42d546066b");
    public static readonly Guid Equipment = new("5d2f3734-5a49-4504-90b1-0676f0f25da7");
    public static readonly Guid MechanicalEquipment = new("de4420ce-02b6-4b12-9cd7-9322118be8fe");
    public static readonly Guid PlumbingFixture = new("b8c7155a-b462-4ff5-bc41-c9c17a9f48fa");
    public static readonly Guid LightingFixture = new("793d3f7c-905d-4d85-a351-b152241dd2e7");
    public static readonly Guid WiringAccessory = new("b00d5c25-92a8-4409-a3b7-7c37ed792c06");
    public static readonly Guid ElectricDistributionBoard = new("96da9155-43c1-42b8-bba2-b4f61fa43acc");
    public static readonly Guid PipeStyle = new("9d6dffb9-4828-40d8-8529-bf5cd2b58c4e");
    public static readonly Guid DuctStyle = new("a999f05a-d730-42e7-bfc8-e4433ebace78");
    public static readonly Guid ElectricalCircuitLineStyle = new("33fb4b37-83f9-422a-81d4-640a152c619e");
    public static readonly Guid PipeFittingStyle = new("b1359bdc-f7ff-43a4-bca0-8d09bc974537");
    public static readonly Guid DuctFittingStyle = new("6c6821a0-ebb9-445b-84a2-ed9eb0938e4f");
    public static readonly Guid PipeAccessoryStyle = new("a31cf7ca-f17b-422a-886a-7a8c362cd49a");
    public static readonly Guid DuctAccessoryStyle = new("6c671391-bfea-4e92-9753-8855c05640a0");
    public static readonly Guid SystemStyle = new("e65c5fad-d4d3-4f43-bd01-b28d0eb95571");
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
    public static readonly Guid EntityName = new("7b57355d-ed38-4ad3-91db-ab538a9c1d98");
    public static readonly Guid MassDensity = new("469788e6-d7a6-4f73-90cc-fa756b14bc5c");
    public static readonly Guid MaterialId = new("d5c738c4-7ffa-46cc-a440-52ba62de6ae0");
    public static readonly Guid LayeredMaterialId = new("2cbc4964-61a0-4553-8103-36e2dd2ab31c");
    public static readonly Guid ProfileId = new("6bfc8884-80d5-4aae-a86c-f2e5ae81a888");
    public static readonly Guid Color = new("87d2df84-d69e-495f-a46e-4468185268c3");
}

public static class Units
{
    public const double MillimetersPerMeter = 1000.0;

    public static double ToMillimeters(double meters) => meters * MillimetersPerMeter;

    public static double ToMeters(double millimeters) => millimeters / MillimetersPerMeter;
}

public sealed record QuantityDefinition(string Name, Guid Id, QuantityType Type, string Unit);

public static class RengaQuantities
{
    public static IReadOnlyList<QuantityDefinition> All { get; } = new[]
    {
        new QuantityDefinition("Area", new Guid("025f4cef-7b6d-45d9-99e6-2cd851306e03"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("Count", new Guid("ca9715c4-6111-4fb7-a159-2405f5a0bb92"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("CrossSectionOverallHeight", new Guid("c1e657ad-9dfe-4b17-84dc-5a0802498536"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("CrossSectionOverallWidth", new Guid("2952d91e-bc58-4a94-832b-f583c22efb44"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("GlazingArea", new Guid("11e1deb2-2030-4f37-b633-3a7ae162437a"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossArea", new Guid("69b66c34-d411-422d-bd4a-cac3f6846fd8"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossCeilingArea", new Guid("3e5395c2-5f6b-4d58-8349-1419a29a47b9"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossCrossSectionArea", new Guid("b38f4236-165a-45ab-8b96-ec0175343404"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossFloorArea", new Guid("89ab9b57-91b1-4f4a-9a45-9c935882231d"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossMass", new Guid("e074e28b-0537-4e37-845a-c7315843ee73"), QuantityType.QuantityType_Mass, "kg"),
        new QuantityDefinition("GrossPerimeter", new Guid("eb5bd744-3c80-46a2-b491-2ca4f11dd13d"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("GrossSideArea", new Guid("0003e98a-a2f7-4477-89c9-3e9ef6533be3"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossSideAreaLeft", new Guid("269e7b41-3dd9-42d1-b0d0-074e89a3a283"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossSideAreaRight", new Guid("9c34e7be-27f7-4950-a51d-dc2d5da8f3e8"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("GrossVolume", new Guid("da41f09a-0e02-40c7-9547-2a0f55b60078"), QuantityType.QuantityType_Volume, "m3"),
        new QuantityDefinition("GrossWallArea", new Guid("4144973d-c4b7-4f11-ab48-8794c9beae43"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("InnerSurfaceArea", new Guid("a5ca8dfb-13c6-408c-a1c0-61e67026b36b"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("InnerSurfaceExternalArea", new Guid("7a643dc2-5524-489d-97b3-7011dcbfff48"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("InnerSurfaceInternalArea", new Guid("1201a2e0-bfb7-4b28-88f6-923571972890"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetArea", new Guid("0aab4bb0-4645-48d6-9dcb-2aca48577e47"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetCeilingArea", new Guid("e6265c5c-7692-49bd-b7a1-0a5f452feedd"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetCrossSectionArea", new Guid("5f1efe77-5e2e-450a-a426-efef5b346ecc"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetFloorArea", new Guid("ea60d526-b527-4896-8e4c-c84a8462b3cc"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetFootprintArea", new Guid("65bf3096-b610-4fb0-b603-9e1fd5c21095"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetMass", new Guid("f9230d52-3d8d-42f0-b498-5f8037610c01"), QuantityType.QuantityType_Mass, "kg"),
        new QuantityDefinition("NetPerimeter", new Guid("3560988c-ca06-4028-a821-726bab038536"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("NetSideArea", new Guid("6d692b46-fc72-4696-a55e-1d3469aa9d8e"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetSideAreaLeft", new Guid("015076c9-4040-45a0-87be-fa4124f6cd4e"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetSideAreaRight", new Guid("23e7d323-fc29-4b06-8ce7-1bcd2a20f029"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NetVolume", new Guid("043401f3-0b2f-402a-aca0-826436822405"), QuantityType.QuantityType_Volume, "m3"),
        new QuantityDefinition("NetWallArea", new Guid("c2975e1e-3293-4ad1-8136-316d191b75cc"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NominalArea", new Guid("fb351198-1a4f-4815-953e-177c17e7641c"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("NominalHeight", new Guid("e8f1a615-3b0c-401c-ad1f-7526f2f073e2"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("NominalLength", new Guid("452f15e3-50e0-4da6-9155-567a00d2d5b7"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("NominalThickness", new Guid("d74737a8-dfce-4966-b044-2c20ae9f52fb"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("NominalWidth", new Guid("17b09070-9ffe-4b1f-bcc9-6cb23fb1d6ec"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("NumberOfRiser", new Guid("e1052ec8-bbfe-4a20-b093-c2752613ae80"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("NumberOfTreads", new Guid("5cb8445e-8543-484a-b4ba-0f3343aa1f3a"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("OuterSurfaceArea", new Guid("de1b2227-310d-4652-a793-c799c5be7036"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("OverallDepth", new Guid("9210a311-a2f8-4741-aa51-bf04ae4d8425"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("OverallHeight", new Guid("ff5dea01-7a9a-447b-9542-14b651dd8484"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("OverallLength", new Guid("337e5138-9799-45cf-94f3-5fb6b4ae3de5"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("OverallWidth", new Guid("c5501a9d-2f69-4990-bbf2-bfdaaf6e16da"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("Perimeter", new Guid("2c072284-132e-4440-8178-e04f64b73320"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("ReinforcementUnitCount", new Guid("91acc5e5-c678-4224-aa1d-f569a9e5ecbf"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("RelativeObjectBaselineBottomElevation", new Guid("8745b6cc-fb9d-4171-84c1-3ff0d88eae16"), QuantityType.QuantityType_Elevation, "m"),
        new QuantityDefinition("RelativeObjectBaselineTopElevation", new Guid("3c9de0a7-be69-43f7-9eb4-72b2313502f4"), QuantityType.QuantityType_Elevation, "m"),
        new QuantityDefinition("RelativeObjectBottomElevation", new Guid("e17d805b-0164-480a-849b-64fd022ec17a"), QuantityType.QuantityType_Elevation, "m"),
        new QuantityDefinition("RelativeObjectTopElevation", new Guid("3ca98238-a2b5-424a-a579-6a0d66ef075a"), QuantityType.QuantityType_Elevation, "m"),
        new QuantityDefinition("SheetCount", new Guid("ec396134-52c2-4527-90ea-462999f02f1c"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("SheetNumber", new Guid("4c19f51b-9732-4d4a-956d-ce0aba12859d"), QuantityType.QuantityType_Count, "count"),
        new QuantityDefinition("SlopeAngle", new Guid("653f7d66-5890-426d-a89d-13ab0d7a1cc1"), QuantityType.QuantityType_Angle, "deg"),
        new QuantityDefinition("TotalLengthOfConductors", new Guid("6686a358-9951-4d62-a44f-9a10fa18628e"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("TotalRebarLength", new Guid("198ff2a0-1c0e-4808-8945-734f6f83dce7"), QuantityType.QuantityType_Length, "m"),
        new QuantityDefinition("TotalRebarMass", new Guid("f1f263af-3a0f-490c-8946-7e1fce328179"), QuantityType.QuantityType_Mass, "kg"),
        new QuantityDefinition("TotalSurfaceArea", new Guid("63afa0b2-3dc4-4500-9ca5-f46fc0ae935a"), QuantityType.QuantityType_Area, "m2"),
        new QuantityDefinition("Volume", new Guid("6e63058d-0ab3-4abd-a9ba-574e1746c5ad"), QuantityType.QuantityType_Volume, "m3")
    };

    public static QuantityDefinition? Find(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Guid.TryParse(trimmed, out var id))
        {
            return All.FirstOrDefault(item => item.Id == id);
        }

        var normalized = Normalize(trimmed);
        return All.FirstOrDefault(item =>
            string.Equals(Normalize(item.Name), normalized, StringComparison.Ordinal));
    }

    private static string Normalize(string value) =>
        new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
