using System.Globalization;
using System.Numerics;
using System.Text;

namespace RengaMcpServer.Renga;

public sealed record SeedLevel(string Name, double ElevationMm);

public sealed record SeedAxis(string Name, double X1, double Y1, double X2, double Y2);

public static class IfcSeedBuilder
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz_$";

    public static string Build(IReadOnlyList<SeedLevel> levels, IReadOnlyList<SeedAxis> axes)
    {
        var effectiveLevels = levels.Count > 0
            ? levels
            : new[] { new SeedLevel("Level 1", 0) };

        ValidateLevels(effectiveLevels);
        ValidateAxes(axes);

        var b = new StepBuilder();

        var person = b.Add("IFCPERSON($,$,'Renga MCP',$,$,$,$,$)");
        var organization = b.Add("IFCORGANIZATION($,'Renga MCP',$,$,$)");
        var actor = b.Add($"IFCPERSONANDORGANIZATION({person},{organization},$)");
        var application = b.Add($"IFCAPPLICATION({organization},'1','Renga MCP seed','RENGA_MCP')");
        var owner = b.Add($"IFCOWNERHISTORY({actor},{application},$,.ADDED.,$,$,$,0)");
        var zero = b.Add("IFCCARTESIANPOINT((0.,0.,0.))");
        var vertical = b.Add("IFCDIRECTION((0.,0.,1.))");
        var horizontal = b.Add("IFCDIRECTION((1.,0.,0.))");
        var coordinateSystem = b.Add($"IFCAXIS2PLACEMENT3D({zero},{vertical},{horizontal})");
        var world = b.Add($"IFCLOCALPLACEMENT($,{coordinateSystem})");
        var context = b.Add($"IFCGEOMETRICREPRESENTATIONCONTEXT($,'Model',3,0.00001,{coordinateSystem},$)");
        var length = b.Add("IFCSIUNIT(*,.LENGTHUNIT.,.MILLI.,.METRE.)");
        var area = b.Add("IFCSIUNIT(*,.AREAUNIT.,$,.SQUARE_METRE.)");
        var volume = b.Add("IFCSIUNIT(*,.VOLUMEUNIT.,$,.CUBIC_METRE.)");
        var units = b.Add($"IFCUNITASSIGNMENT(({length},{area},{volume}))");
        var project = b.Add(
            $"IFCPROJECT('{NewIfcGuid()}',{owner},'Renga MCP project',$,$,$,$,({context}),{units})");
        var site = b.Add(
            $"IFCSITE('{NewIfcGuid()}',{owner},'Site',$,$,{world},$,$,.ELEMENT.,$,$,$,$,$)");
        var buildingPlacement = b.Add($"IFCLOCALPLACEMENT({world},{coordinateSystem})");
        var building = b.Add(
            $"IFCBUILDING('{NewIfcGuid()}',{owner},'Building',$,$,{buildingPlacement},$,$,.ELEMENT.,$,$,$)");
        b.Add($"IFCRELAGGREGATES('{NewIfcGuid()}',{owner},$,$,{project},({site}))");
        b.Add($"IFCRELAGGREGATES('{NewIfcGuid()}',{owner},$,$,{site},({building}))");

        var storeys = new List<string>();
        for (var index = 0; index < effectiveLevels.Count; index++)
        {
            var level = effectiveLevels[index];
            var elevation = StepReal(level.ElevationMm);
            var origin = b.Add($"IFCCARTESIANPOINT((0.,0.,{elevation}))");
            var axis = b.Add($"IFCAXIS2PLACEMENT3D({origin},{vertical},{horizontal})");
            var placement = b.Add($"IFCLOCALPLACEMENT({buildingPlacement},{axis})");
            var storey = b.Add(
                $"IFCBUILDINGSTOREY('{NewIfcGuid()}',{owner},{IfcString(level.Name)},$,$,{placement},$,$,.ELEMENT.,{elevation})");
            storeys.Add(storey);

            var slabPlacement = b.Add($"IFCLOCALPLACEMENT({placement},{coordinateSystem})");
            var profileOrigin = b.Add("IFCCARTESIANPOINT((3000.,2000.))");
            var profileAxis = b.Add($"IFCAXIS2PLACEMENT2D({profileOrigin},$)");
            var profile = b.Add(
                $"IFCRECTANGLEPROFILEDEF(.AREA.,'Import carrier',{profileAxis},6000.,4000.)");
            var solid = b.Add($"IFCEXTRUDEDAREASOLID({profile},{coordinateSystem},{vertical},200.)");
            var representation = b.Add($"IFCSHAPEREPRESENTATION({context},'Body','SweptSolid',({solid}))");
            var shape = b.Add($"IFCPRODUCTDEFINITIONSHAPE($,$,({representation}))");
            var slab = b.Add(
                $"IFCSLAB('{NewIfcGuid()}',{owner},'Disposable import carrier',$,$,{slabPlacement},{shape},'C{index}',.FLOOR.)");
            b.Add(
                $"IFCRELCONTAINEDINSPATIALSTRUCTURE('{NewIfcGuid()}',{owner},$,$,({slab}),{storey})");
        }

        b.Add($"IFCRELAGGREGATES('{NewIfcGuid()}',{owner},$,$,{building},({string.Join(',', storeys)}))");

        if (axes.Count > 0)
        {
            var uAxes = new List<string>();
            var vAxes = new List<string>();
            var curves = new List<string>();

            foreach (var spec in axes)
            {
                var p1 = b.Add(
                    $"IFCCARTESIANPOINT(({StepReal(spec.X1)},{StepReal(spec.Y1)}))");
                var p2 = b.Add(
                    $"IFCCARTESIANPOINT(({StepReal(spec.X2)},{StepReal(spec.Y2)}))");
                var curve = b.Add($"IFCPOLYLINE(({p1},{p2}))");
                curves.Add(curve);
                var axis = b.Add($"IFCGRIDAXIS({IfcString(spec.Name)},{curve},.T.)");

                var dx = Math.Abs(spec.X2 - spec.X1);
                var dy = Math.Abs(spec.Y2 - spec.Y1);
                if (dx >= dy)
                {
                    uAxes.Add(axis);
                }
                else
                {
                    vAxes.Add(axis);
                }
            }

            var curveSet = b.Add($"IFCGEOMETRICCURVESET(({string.Join(',', curves)}))");
            var gridRepresentation = b.Add(
                $"IFCSHAPEREPRESENTATION({context},'FootPrint','GeometricCurveSet',({curveSet}))");
            var gridShape = b.Add(
                $"IFCPRODUCTDEFINITIONSHAPE($,$,({gridRepresentation}))");
            var gridPlacement = b.Add($"IFCLOCALPLACEMENT({buildingPlacement},{coordinateSystem})");
            var uList = uAxes.Count > 0 ? $"({string.Join(',', uAxes)})" : "$";
            var vList = vAxes.Count > 0 ? $"({string.Join(',', vAxes)})" : "$";
            var grid = b.Add(
                $"IFCGRID('{NewIfcGuid()}',{owner},'Coordinate axes',$,$,{gridPlacement},{gridShape},{uList},{vList},$,.RECTANGULAR.)");
            b.Add(
                $"IFCRELCONTAINEDINSPATIALSTRUCTURE('{NewIfcGuid()}',{owner},$,$,({grid}),{storeys[0]})");
        }

        var header =
            "ISO-10303-21;\nHEADER;\n" +
            "FILE_DESCRIPTION(('ViewDefinition [DesignTransferView]'),'2;1');\n" +
            "FILE_NAME('renga-mcp-seed.ifc','2026-01-01T00:00:00',('Renga MCP'),('Renga MCP')," +
            "'Renga MCP','Renga MCP','');\n" +
            "FILE_SCHEMA(('IFC4'));\nENDSEC;\nDATA;\n";

        return header + string.Join("\n", b.Lines) + "\nENDSEC;\nEND-ISO-10303-21;\n";
    }

    private static void ValidateLevels(IReadOnlyList<SeedLevel> levels)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var elevations = new HashSet<double>();
        foreach (var level in levels)
        {
            if (string.IsNullOrWhiteSpace(level.Name))
            {
                throw new ArgumentException("Level name must be non-empty.");
            }

            if (!double.IsFinite(level.ElevationMm))
            {
                throw new ArgumentException("Level elevation must be finite.");
            }

            if (!names.Add(level.Name))
            {
                throw new ArgumentException($"Duplicate level name: {level.Name}");
            }

            if (!elevations.Add(level.ElevationMm == 0 ? 0 : level.ElevationMm))
            {
                throw new ArgumentException($"Duplicate level elevation: {level.ElevationMm} mm");
            }
        }
    }

    private static void ValidateAxes(IReadOnlyList<SeedAxis> axes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var axis in axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name))
            {
                throw new ArgumentException("Axis name must be non-empty.");
            }

            if (!names.Add(axis.Name))
            {
                throw new ArgumentException($"Duplicate axis name: {axis.Name}");
            }

            if (!double.IsFinite(axis.X1) || !double.IsFinite(axis.Y1) ||
                !double.IsFinite(axis.X2) || !double.IsFinite(axis.Y2))
            {
                throw new ArgumentException("Axis coordinates must be finite.");
            }

            if (Math.Abs(axis.X2 - axis.X1) < 1e-6 && Math.Abs(axis.Y2 - axis.Y1) < 1e-6)
            {
                throw new ArgumentException($"Axis {axis.Name} has zero length.");
            }
        }
    }

    private static string IfcString(string value)
    {
        var result = new StringBuilder("'");
        foreach (var ch in value)
        {
            var number = (int)ch;
            if (ch == '\'')
            {
                result.Append("''");
            }
            else if (number is >= 32 and <= 126 && ch != '\\')
            {
                result.Append(ch);
            }
            else if (number <= 0xFFFF)
            {
                result.Append("\\X2\\").Append(number.ToString("X4", CultureInfo.InvariantCulture)).Append("\\X0\\");
            }
            else
            {
                result.Append("\\X4\\").Append(number.ToString("X8", CultureInfo.InvariantCulture)).Append("\\X0\\");
            }
        }

        return result.Append('\'').ToString();
    }

    private static string StepReal(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture).ToUpperInvariant();
        var exponentIndex = text.IndexOf('E');
        var mantissa = exponentIndex >= 0 ? text[..exponentIndex] : text;
        var exponent = exponentIndex >= 0 ? text[exponentIndex..] : string.Empty;
        if (!mantissa.Contains('.'))
        {
            mantissa += ".";
        }

        return mantissa + exponent;
    }

    private static string NewIfcGuid()
    {
        var value = new BigInteger(Guid.NewGuid().ToByteArray(), isUnsigned: true, isBigEndian: false);
        var result = new char[22];
        for (var i = 21; i >= 0; i--)
        {
            result[i] = Alphabet[(int)(value & 63)];
            value >>= 6;
        }

        return new string(result);
    }

    private sealed class StepBuilder
    {
        public List<string> Lines { get; } = new();

        public string Add(string entity)
        {
            Lines.Add($"#{Lines.Count + 1}={entity};");
            return $"#{Lines.Count}";
        }
    }
}
