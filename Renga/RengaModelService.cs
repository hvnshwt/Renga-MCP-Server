using System.Globalization;
using System.Text;
using System.Text.Json;
using Renga;

namespace RengaMcpServer.Renga;

public sealed class RengaModelService
{
    private readonly RengaConnector _connector;
    private SeedState _state = new();
    private Guid _stateProjectId = Guid.Empty;

    public RengaModelService(RengaConnector connector)
    {
        _connector = connector;
    }

    public object Ping()
    {
        var app = _connector.Connect();
        var result = new Dictionary<string, object?>
        {
            ["connected"] = true,
            ["productName"] = app.ProductName,
            ["version"] = app.VersionS,
            ["visible"] = app.Visible,
            ["hasProject"] = app.HasProject()
        };

        if (!app.HasProject())
        {
            return result;
        }

        var project = app.Project;
        var model = project.Model;
        EnsureState(project, model);

        result["project"] = DescribeProject(project, model);
        result["levels"] = ReadLevels(model).Select(ToDictionary).ToArray();
        result["axes"] = ReadAxes(model).Select(ToDictionary).ToArray();
        result["seed"] = new Dictionary<string, object?>
        {
            ["managed"] = _state.Managed,
            ["freshDefaultProject"] = _state.FreshDefault,
            ["trackedLevels"] = _state.Levels.Select(ToDictionary).ToArray(),
            ["trackedAxes"] = _state.Axes.Select(ToDictionary).ToArray(),
            ["axesGeometryKnown"] = !_state.AxesLocked
        };

        return result;
    }

    public object NewProject()
    {
        _connector.NewProject();
        _stateProjectId = Guid.Empty;
        var project = _connector.RequireProject();
        var model = project.Model;
        EnsureState(project, model);
        _state.FreshDefault = _state.Levels.Count == 1 && !HasGeometry(model);

        return new Dictionary<string, object?>
        {
            ["created"] = true,
            ["project"] = DescribeProject(project, model),
            ["levels"] = ReadLevels(model).Select(ToDictionary).ToArray(),
            ["message"] = "New project created. Create levels and axes before creating walls, columns, beams, or floors."
        };
    }

    public object OpenProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RengaException("Project path must be non-empty.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new RengaException($"Project file not found: {fullPath}");
        }

        var app = _connector.Connect();
        if (app.HasProject())
        {
            _connector.Check(app.CloseProject(true), "CloseProject");
        }

        _connector.Check(app.OpenProject(fullPath), "OpenProject");
        if (!app.HasProject())
        {
            throw new RengaException("OpenProject returned without an open project.");
        }

        _stateProjectId = Guid.Empty;
        var project = app.Project;
        var model = project.Model;
        EnsureState(project, model);

        return new Dictionary<string, object?>
        {
            ["opened"] = true,
            ["project"] = DescribeProject(project, model),
            ["levels"] = ReadLevels(model).Select(ToDictionary).ToArray(),
            ["axes"] = ReadAxes(model).Select(ToDictionary).ToArray()
        };
    }

    public object CreateLevel(string name, double elevationMeters)
    {
        name = RequireName(name, "name");
        var elevationMm = Units.ToMillimeters(RequireFinite(elevationMeters, "elevation"));

        var project = _connector.RequireProject();
        var model = project.Model;
        EnsureState(project, model);

        if (HasGeometry(model))
        {
            throw new RengaException(
                "Cannot create a level after geometry exists. Renga's public API has no writable level elevation; " +
                "levels are created through an official IFC seed import, which rebuilds the level/axis skeleton. " +
                "Create all levels and axes first, then create walls, columns, beams, and floors.");
        }

        if (_state.AxesLocked)
        {
            throw new RengaException(
                "The current project already contains axes whose coordinates are unknown to this session. " +
                "Create a new project to rebuild the level/axis skeleton.");
        }

        if (_state.FreshDefault && !_state.Managed && _state.Levels.Count == 1)
        {
            _state.Levels.Clear();
        }

        if (_state.Levels.Any(level => string.Equals(level.Name, name, StringComparison.Ordinal)))
        {
            throw new RengaException($"A level named '{name}' already exists.");
        }

        if (_state.Levels.Any(level => Math.Abs(level.ElevationMm - elevationMm) < 1e-6))
        {
            throw new RengaException(
                $"A level at elevation {elevationMeters.ToString(CultureInfo.InvariantCulture)} m already exists.");
        }

        var backup = _state.Levels.ToList();
        _state.Levels.Add(new SeedLevel(name, elevationMm));
        try
        {
            var rebuilt = (Dictionary<string, object?>)RebuildSeed(project, model);
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["level"] = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["elevation"] = elevationMeters
                },
                ["project"] = rebuilt["project"],
                ["levels"] = rebuilt["levels"],
                ["axes"] = rebuilt["axes"]
            };
        }
        catch
        {
            _state.Levels = backup;
            throw;
        }
    }

    public object CreateAxis(string name, double x1, double y1, double x2, double y2)
    {
        name = RequireName(name, "name");
        var ax1 = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var ay1 = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ax2 = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ay2 = Units.ToMillimeters(RequireFinite(y2, "y2"));
        if (Math.Abs(ax2 - ax1) < 1e-6 && Math.Abs(ay2 - ay1) < 1e-6)
        {
            throw new RengaException("Axis endpoints must be different.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        EnsureState(project, model);

        if (HasGeometry(model))
        {
            throw new RengaException(
                "Cannot create an axis after geometry exists. Renga's public API cannot construct Axis objects directly; " +
                "axes are created through an official IFC seed import, which rebuilds the level/axis skeleton. " +
                "Create all levels and axes first, then create walls, columns, beams, and floors.");
        }

        if (_state.AxesLocked)
        {
            throw new RengaException(
                "The current project already contains axes whose coordinates are unknown to this session. " +
                "Create a new project to rebuild the level/axis skeleton.");
        }

        if (_state.Axes.Any(axis => string.Equals(axis.Name, name, StringComparison.Ordinal)))
        {
            throw new RengaException($"An axis named '{name}' already exists.");
        }

        if (_state.FreshDefault && !_state.Managed && _state.Levels.Count == 1)
        {
            _state.Levels.Clear();
        }

        var backup = _state.Axes.ToList();
        var backupLevels = _state.Levels.ToList();
        _state.Axes.Add(new SeedAxis(name, ax1, ay1, ax2, ay2));
        try
        {
            var rebuilt = (Dictionary<string, object?>)RebuildSeed(project, model);
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["axis"] = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["x1"] = x1,
                    ["y1"] = y1,
                    ["x2"] = x2,
                    ["y2"] = y2
                },
                ["project"] = rebuilt["project"],
                ["levels"] = rebuilt["levels"],
                ["axes"] = rebuilt["axes"]
            };
        }
        catch
        {
            _state.Axes = backup;
            _state.Levels = backupLevels;
            throw;
        }
    }

    public object CreateWall(
        double x1,
        double y1,
        double x2,
        double y2,
        int levelId,
        double heightMeters,
        double? thicknessMeters)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (heightMm <= 0)
        {
            throw new RengaException("Wall height must be positive.");
        }

        var thicknessMm = thicknessMeters is double requestedThickness
            ? Units.ToMillimeters(RequireFinite(requestedThickness, "thickness"))
            : 0;
        if (thicknessMeters is not null && thicknessMm <= 0)
        {
            throw new RengaException("Wall thickness must be positive.");
        }

        var dx = ex - sx;
        var dy = ey - sy;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            throw new RengaException("Wall start and end points must be different.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = FindLevel(model, levelId)
            ?? throw new RengaException($"Level with ID {levelId} was not found.");

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Wall;
            args.HostObjectId = levelId;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = sx, Y = sy, Z = 0 },
                xAxis = new Vector3D { X = dx / length, Y = dy / length, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var wall = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the wall. " + _connector.LastError);

            if (wall is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created wall does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateLine2D(0, 0, length, 0));
            SetDoubleParameter(wall, RengaParams.WallHeight, heightMm);
            if (thicknessMm > 0)
            {
                SetDoubleParameter(wall, RengaParams.WallThickness, thicknessMm);
            }

            op.Apply();
            applied = true;

            var levelObject = wall as ILevelObject;
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = wall.Id,
                ["uniqueId"] = wall.UniqueIdS,
                ["type"] = wall.ObjectTypeS,
                ["levelId"] = levelObject?.LevelId ?? levelId,
                ["height"] = heightMeters,
                ["thickness"] = thicknessMeters,
                ["length"] = Units.ToMeters(length),
                ["start"] = new { x = x1, y = y1 },
                ["end"] = new { x = x2, y = y2 },
                ["baseline"] = DescribeBaseline2D(wall)
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateColumn(
        double x,
        double y,
        double heightMeters,
        int? levelId,
        double? elevationMeters,
        int? styleId)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (heightMm <= 0)
        {
            throw new RengaException("Column height must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, elevationMeters);
        var localZ = elevationMeters is double elevation
            ? Units.ToMillimeters(elevation) - level.ElevationMm
            : 0;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Column;
            args.HostObjectId = level.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = localZ },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var resolvedStyleId = styleId is int requestedStyle && requestedStyle > 0
                ? requestedStyle
                : FirstStyleId(project.ColumnStyles);
            if (resolvedStyleId > 0)
            {
                args.StyleId = resolvedStyleId;
            }

            var column = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the column. " + _connector.LastError);

            SetDoubleParameter(column, RengaParams.ColumnHeight, heightMm);
            op.Apply();
            applied = true;

            var levelObject = column as ILevelObject;
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = column.Id,
                ["uniqueId"] = column.UniqueIdS,
                ["type"] = column.ObjectTypeS,
                ["levelId"] = levelObject?.LevelId ?? level.Id,
                ["levelName"] = level.Name,
                ["x"] = x,
                ["y"] = y,
                ["height"] = heightMeters,
                ["elevationAboveLevel"] = Units.ToMeters(levelObject?.ElevationAboveLevel ?? localZ),
                ["styleId"] = resolvedStyleId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateBeam(
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        int? levelId,
        int? styleId)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var sz = Units.ToMillimeters(RequireFinite(z1, "z1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var ez = Units.ToMillimeters(RequireFinite(z2, "z2"));

        var dx = ex - sx;
        var dy = ey - sy;
        var dz = ez - sz;
        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-6)
        {
            throw new RengaException("Beam start and end points must be different.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, z1);
        var localStartZ = sz - level.ElevationMm;
        var localEndZ = ez - level.ElevationMm;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Beam;
            args.HostObjectId = level.Id;
            args.Placement3D = IdentityPlacement();

            var resolvedStyleId = styleId is int requestedStyle && requestedStyle > 0
                ? requestedStyle
                : FirstStyleId(project.BeamStyles);
            if (resolvedStyleId > 0)
            {
                args.StyleId = resolvedStyleId;
            }

            var beam = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the beam. " + _connector.LastError);

            if (beam is not IBaseline3DObject baseline)
            {
                throw new RengaException("The created beam does not expose IBaseline3DObject.");
            }

            var curve = _connector.Connect().Math.CreateLineSegment3D(
                new Point3D { X = sx, Y = sy, Z = localStartZ },
                new Point3D { X = ex, Y = ey, Z = localEndZ });
            baseline.SetBaselineInCS(IdentityPlacement(), curve);

            op.Apply();
            applied = true;

            var levelObject = beam as ILevelObject;
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = beam.Id,
                ["uniqueId"] = beam.UniqueIdS,
                ["type"] = beam.ObjectTypeS,
                ["levelId"] = levelObject?.LevelId ?? level.Id,
                ["levelName"] = level.Name,
                ["length"] = Units.ToMeters(length),
                ["start"] = new { x = x1, y = y1, z = z1 },
                ["end"] = new { x = x2, y = y2, z = z2 },
                ["styleId"] = resolvedStyleId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateDoor(
        int wallId,
        double x,
        double y,
        double widthMeters,
        double heightMeters,
        double? offsetMeters,
        int? styleId)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var widthMm = Units.ToMillimeters(RequireFinite(widthMeters, "width"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));
        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Door width and height must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var wall = FindObject(model, wallId)
            ?? throw new RengaException($"Wall with ID {wallId} was not found.");
        if (wall.ObjectType != RengaIds.Wall)
        {
            throw new RengaException($"Object {wallId} is not a wall.");
        }

        var direction = GetWallDirection(wall);
        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Door;
            args.HostObjectId = wallId;
            var resolvedStyleId = styleId is int requestedStyle && requestedStyle > 0
                ? requestedStyle
                : 0;
            args.StyleId = resolvedStyleId;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = offsetMm },
                xAxis = new Vector3D { X = direction.X, Y = direction.Y, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var door = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the door. " + _connector.LastError);

            SetDoubleParameter(door, RengaParams.DoorWidth, widthMm);
            SetDoubleParameter(door, RengaParams.DoorHeight, heightMm);
            SetDoubleParameter(door, RengaParams.Offset, offsetMm);

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = door.Id,
                ["uniqueId"] = door.UniqueIdS,
                ["type"] = door.ObjectTypeS,
                ["wallId"] = wallId,
                ["x"] = x,
                ["y"] = y,
                ["width"] = widthMeters,
                ["height"] = heightMeters,
                ["offset"] = Units.ToMeters(offsetMm),
                ["styleId"] = resolvedStyleId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateWindow(
        int wallId,
        double x,
        double y,
        double widthMeters,
        double heightMeters,
        double? sillMeters,
        int? styleId)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var widthMm = Units.ToMillimeters(RequireFinite(widthMeters, "width"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        var sillMm = Units.ToMillimeters(RequireFinite(sillMeters ?? 0.9, "sill"));
        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Window width and height must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var wall = FindObject(model, wallId)
            ?? throw new RengaException($"Wall with ID {wallId} was not found.");
        if (wall.ObjectType != RengaIds.Wall)
        {
            throw new RengaException($"Object {wallId} is not a wall.");
        }

        var direction = GetWallDirection(wall);
        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Window;
            args.HostObjectId = wallId;
            var resolvedStyleId = styleId is int requestedStyle && requestedStyle > 0
                ? requestedStyle
                : 0;
            args.StyleId = resolvedStyleId;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = sillMm },
                xAxis = new Vector3D { X = direction.X, Y = direction.Y, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var window = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the window. " + _connector.LastError);

            SetDoubleParameter(window, RengaParams.WindowWidth, widthMm);
            SetDoubleParameter(window, RengaParams.WindowHeight, heightMm);
            SetDoubleParameter(window, RengaParams.Offset, sillMm);

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = window.Id,
                ["uniqueId"] = window.UniqueIdS,
                ["type"] = window.ObjectTypeS,
                ["wallId"] = wallId,
                ["x"] = x,
                ["y"] = y,
                ["width"] = widthMeters,
                ["height"] = heightMeters,
                ["sill"] = Units.ToMeters(sillMm),
                ["styleId"] = resolvedStyleId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateRoof(
        int levelId,
        double thicknessMeters,
        IReadOnlyList<(double X, double Y)>? points,
        double? x1,
        double? y1,
        double? x2,
        double? y2,
        double? offsetMeters)
    {
        var thicknessMm = Units.ToMillimeters(RequireFinite(thicknessMeters, "thickness"));
        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));
        if (thicknessMm <= 0)
        {
            throw new RengaException("Roof thickness must be positive.");
        }

        IReadOnlyList<(double X, double Y)> contour;
        if (points is { Count: >= 3 })
        {
            contour = points;
        }
        else if (x1 is double rx1 && y1 is double ry1 && x2 is double rx2 && y2 is double ry2)
        {
            contour = new[]
            {
                (rx1, ry1),
                (rx2, ry1),
                (rx2, ry2),
                (rx1, ry2)
            };
        }
        else
        {
            throw new RengaException("Provide either a points array with at least 3 points or x1, y1, x2, y2.");
        }

        var polygon = contour
            .Select(point => (
                X: Units.ToMillimeters(RequireFinite(point.X, "point.x")),
                Y: Units.ToMillimeters(RequireFinite(point.Y, "point.y"))))
            .ToList();

        if (polygon.Count > 1 &&
            Math.Abs(polygon[0].X - polygon[^1].X) < 1e-6 &&
            Math.Abs(polygon[0].Y - polygon[^1].Y) < 1e-6)
        {
            polygon.RemoveAt(polygon.Count - 1);
        }

        if (polygon.Count < 3)
        {
            throw new RengaException("Roof contour requires at least 3 distinct points.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = FindLevel(model, levelId)
            ?? throw new RengaException($"Level with ID {levelId} was not found.");
        var levelName = ((ILevel)level).LevelName;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Roof;
            args.HostObjectId = levelId;
            args.Placement3D = IdentityPlacement();

            var roof = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the roof. " + _connector.LastError);

            if (roof is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created roof does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateClosedPolygon(polygon));
            SetDoubleParameter(roof, RengaParams.RoofThickness, thicknessMm);
            SetDoubleParameter(roof, RengaParams.Offset, offsetMm);

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = roof.Id,
                ["uniqueId"] = roof.UniqueIdS,
                ["type"] = roof.ObjectTypeS,
                ["levelId"] = levelId,
                ["levelName"] = levelName,
                ["thickness"] = thicknessMeters,
                ["offset"] = Units.ToMeters(offsetMm),
                ["points"] = polygon
                    .Select(point => new { x = Units.ToMeters(point.X), y = Units.ToMeters(point.Y) })
                    .ToArray(),
                ["area"] = PolygonArea(polygon) / 1_000_000.0
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateFloor(
        int levelId,
        double thicknessMeters,
        IReadOnlyList<(double X, double Y)>? points,
        double? x1,
        double? y1,
        double? x2,
        double? y2)
    {
        var thicknessMm = Units.ToMillimeters(RequireFinite(thicknessMeters, "thickness"));
        if (thicknessMm <= 0)
        {
            throw new RengaException("Floor thickness must be positive.");
        }

        IReadOnlyList<(double X, double Y)> contour;
        if (points is { Count: >= 3 })
        {
            contour = points;
        }
        else if (x1 is double rx1 && y1 is double ry1 && x2 is double rx2 && y2 is double ry2)
        {
            contour = new[]
            {
                (rx1, ry1),
                (rx2, ry1),
                (rx2, ry2),
                (rx1, ry2)
            };
        }
        else
        {
            throw new RengaException("Provide either a points array with at least 3 points or x1, y1, x2, y2.");
        }

        var polygon = contour
            .Select(point => (
                X: Units.ToMillimeters(RequireFinite(point.X, "point.x")),
                Y: Units.ToMillimeters(RequireFinite(point.Y, "point.y"))))
            .ToList();

        if (polygon.Count > 1 &&
            Math.Abs(polygon[0].X - polygon[^1].X) < 1e-6 &&
            Math.Abs(polygon[0].Y - polygon[^1].Y) < 1e-6)
        {
            polygon.RemoveAt(polygon.Count - 1);
        }

        if (polygon.Count < 3)
        {
            throw new RengaException("Floor contour requires at least 3 distinct points.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = FindLevel(model, levelId)
            ?? throw new RengaException($"Level with ID {levelId} was not found.");
        var levelName = ((ILevel)level).LevelName;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Floor;
            args.HostObjectId = levelId;
            args.Placement3D = IdentityPlacement();

            var floor = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the floor. " + _connector.LastError);

            if (floor is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created floor does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateClosedPolygon(polygon));
            SetDoubleParameter(floor, RengaParams.FloorThickness, thicknessMm);

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = floor.Id,
                ["uniqueId"] = floor.UniqueIdS,
                ["type"] = floor.ObjectTypeS,
                ["levelId"] = levelId,
                ["levelName"] = levelName,
                ["thickness"] = thicknessMeters,
                ["points"] = polygon
                    .Select(point => new { x = Units.ToMeters(point.X), y = Units.ToMeters(point.Y) })
                    .ToArray(),
                ["area"] = PolygonArea(polygon) / 1_000_000.0
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateOpening(
        int hostId,
        IReadOnlyList<(double X, double Y)>? points,
        double? x1,
        double? y1,
        double? x2,
        double? y2,
        double? depthMeters,
        double? offsetMeters,
        string? mark)
    {
        var polygon = ResolvePlanContour(points, x1, y1, x2, y2, "an opening");
        var depthMm = depthMeters is double requestedDepth
            ? Units.ToMillimeters(RequireFinite(requestedDepth, "depth"))
            : (double?)null;
        var offsetMm = offsetMeters is double requestedOffset
            ? Units.ToMillimeters(RequireFinite(requestedOffset, "offset"))
            : (double?)null;
        if (depthMm is double depth && depth <= 0)
        {
            throw new RengaException("Opening depth must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var host = FindObject(model, hostId)
            ?? throw new RengaException($"Host object with ID {hostId} was not found.");
        if (host.ObjectType != RengaIds.Floor && host.ObjectType != RengaIds.Roof)
        {
            throw new RengaException(
                $"Openings can be hosted on floors and roofs, not on {TypeName(host.ObjectType)}. " +
                "For wall apertures use renga_create_door or renga_create_window.");
        }

        var center = BoundingBoxCenter(polygon);
        var localPolygon = polygon
            .Select(point => (X: point.X - center.X, Y: point.Y - center.Y))
            .ToList();

        var hostElevationMm = 0.0;
        if (host is ILevelObject hostLevelObject &&
            FindLevel(model, hostLevelObject.LevelId) is ILevel hostLevel)
        {
            hostElevationMm = hostLevel.Elevation + hostLevelObject.ElevationAboveLevel;
        }

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Opening;
            args.HostObjectId = hostId;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = center.X, Y = center.Y, Z = hostElevationMm },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var opening = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the opening. " + _connector.LastError);
            if (opening is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created opening does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateClosedPolygon(localPolygon));
            if (depthMm is double openingDepth)
            {
                SetDoubleParameterByName(opening, "OpeningDepth", openingDepth);
            }

            if (offsetMm is double verticalOffset)
            {
                SetDoubleParameterByName(opening, "Offset", verticalOffset);
            }

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(opening, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = opening.Id,
                ["uniqueId"] = opening.UniqueIdS,
                ["type"] = opening.ObjectTypeS,
                ["hostId"] = hostId,
                ["hostType"] = TypeName(host.ObjectType),
                ["depth"] = depthMeters,
                ["offset"] = offsetMeters,
                ["area"] = PolygonArea(polygon) / 1_000_000.0,
                ["perimeter"] = Units.ToMeters(Perimeter(polygon))
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreatePlate(
        int levelId,
        double x,
        double y,
        double? thicknessMeters,
        double? elevationMeters,
        int? styleId,
        int? materialId,
        string? mark)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var thicknessMm = thicknessMeters is double requestedThickness
            ? Units.ToMillimeters(RequireFinite(requestedThickness, "thickness"))
            : (double?)null;
        if (thicknessMm is double thickness && thickness <= 0)
        {
            throw new RengaException("Plate thickness must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, elevationMeters);
        var localZ = elevationMeters is double elevation
            ? Units.ToMillimeters(elevation) - level.ElevationMm
            : 0;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Plate;
            args.HostObjectId = level.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = localZ },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var resolvedStyleId = styleId is int requestedStyle && requestedStyle > 0
                ? requestedStyle
                : 0;
            if (resolvedStyleId > 0)
            {
                args.StyleId = resolvedStyleId;
            }

            var plate = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the plate. " + _connector.LastError);

            if (thicknessMm is double plateThickness)
            {
                SetDoubleParameterByName(plate, "PlateThickness", plateThickness);
            }

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(plate, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            ApplyLateParameters(project, plate, Array.Empty<(string, int)>(), Array.Empty<(string, double)>(), materialId);

            var levelObject = plate as ILevelObject;
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = plate.Id,
                ["uniqueId"] = plate.UniqueIdS,
                ["type"] = plate.ObjectTypeS,
                ["levelId"] = levelObject?.LevelId ?? level.Id,
                ["levelName"] = level.Name,
                ["x"] = x,
                ["y"] = y,
                ["elevationAboveLevel"] = Units.ToMeters(levelObject?.ElevationAboveLevel ?? localZ),
                ["thickness"] = thicknessMeters,
                ["styleId"] = resolvedStyleId,
                ["materialId"] = materialId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateWallFoundation(
        int levelId,
        double x1,
        double y1,
        double x2,
        double y2,
        double thicknessMeters,
        double heightMeters,
        double? offsetMeters,
        string? shape,
        double? thicknessTopMeters,
        string? position,
        double? horizontalOffsetMeters,
        int? materialId,
        string? mark)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var thicknessMm = Units.ToMillimeters(RequireFinite(thicknessMeters, "thickness"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (thicknessMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Wall foundation thickness and height must be positive.");
        }

        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));
        var horizontalOffsetMm = Units.ToMillimeters(RequireFinite(horizontalOffsetMeters ?? 0, "horizontalOffset"));
        var shapeValue = string.IsNullOrWhiteSpace(shape) ? (int?)null : ParseWallFoundationShape(shape);
        var positionValue = string.IsNullOrWhiteSpace(position) ? (int?)null : ParsePosition(position);
        var topMm = thicknessTopMeters is double requestedTop
            ? Units.ToMillimeters(RequireFinite(requestedTop, "thicknessTop"))
            : (double?)null;
        if (topMm is not null && shapeValue is null)
        {
            shapeValue = 1;
        }

        if (shapeValue == 1 && topMm is null)
        {
            topMm = thicknessMm;
        }

        if (topMm is double top && top <= 0)
        {
            throw new RengaException("Wall foundation top thickness must be positive.");
        }

        var project = _connector.RequireProject();
        var model = project.Model;
        ResolveLevel(model, levelId, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = CreateBaselineArgs(model, RengaIds.WallFoundation, levelId, sx, sy, ex, ey, out var lengthMm);
            var foundation = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the wall foundation. " + _connector.LastError);
            if (foundation is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created wall foundation does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateLine2D(0, 0, lengthMm, 0));
            SetDoubleParameterByName(foundation, "WallFoundationThickness", thicknessMm);
            SetDoubleParameterByName(foundation, "WallFoundationHeight", heightMm);
            SetDoubleParameterByName(foundation, "Offset", offsetMm);
            SetDoubleParameterByName(foundation, "WallFoundationHorizontalOffset", horizontalOffsetMm);

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(foundation, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            var lateInts = new List<(string, int)>();
            if (shapeValue is int shapeCode)
            {
                lateInts.Add(("WallFoundationShape", shapeCode));
            }

            if (positionValue is int positionCode)
            {
                lateInts.Add(("WallFoundationPositionRelativeToBaseline", positionCode));
            }

            var lateDoubles = new List<(string, double)>();
            if (topMm is double topThickness)
            {
                lateDoubles.Add(("WallFoundationThicknessTop", topThickness));
            }

            ApplyLateParameters(project, foundation, lateInts, lateDoubles, materialId);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = foundation.Id,
                ["uniqueId"] = foundation.UniqueIdS,
                ["type"] = foundation.ObjectTypeS,
                ["levelId"] = levelId,
                ["length"] = Units.ToMeters(lengthMm),
                ["thickness"] = thicknessMeters,
                ["thicknessTop"] = topMm is double t ? Units.ToMeters(t) : null,
                ["height"] = heightMeters,
                ["shape"] = shape,
                ["position"] = position,
                ["materialId"] = materialId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateIsolatedFoundation(
        int levelId,
        double x,
        double y,
        double widthMeters,
        double depthMeters,
        double heightMeters,
        double? elevationMeters,
        string? shape,
        double? widthTopMeters,
        double? depthTopMeters,
        double? rotationDegrees,
        int? materialId,
        string? mark)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var widthMm = Units.ToMillimeters(RequireFinite(widthMeters, "width"));
        var depthMm = Units.ToMillimeters(RequireFinite(depthMeters, "depth"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (widthMm <= 0 || depthMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Isolated foundation width, depth, and height must be positive.");
        }

        var shapeValue = string.IsNullOrWhiteSpace(shape) ? (int?)null : ParseWallFoundationShape(shape);
        var widthTopMm = widthTopMeters is double requestedWidthTop
            ? Units.ToMillimeters(RequireFinite(requestedWidthTop, "widthTop"))
            : (double?)null;
        var depthTopMm = depthTopMeters is double requestedDepthTop
            ? Units.ToMillimeters(RequireFinite(requestedDepthTop, "depthTop"))
            : (double?)null;
        if ((widthTopMm is not null || depthTopMm is not null) && shapeValue is null)
        {
            shapeValue = 1;
        }

        if (shapeValue == 1)
        {
            widthTopMm ??= widthMm;
            depthTopMm ??= depthMm;
        }

        var rotation = RequireFinite(rotationDegrees ?? 0, "rotation");

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, elevationMeters);
        var localZ = elevationMeters is double elevation
            ? Units.ToMillimeters(elevation) - level.ElevationMm
            : 0;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.IsolatedFoundation;
            args.HostObjectId = level.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = localZ },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var foundation = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the isolated foundation. " + _connector.LastError);

            SetDoubleParameterByName(foundation, "IsolatedFoundationWidth", widthMm);
            SetDoubleParameterByName(foundation, "IsolatedFoundationDepth", depthMm);
            SetDoubleParameterByName(foundation, "IsolatedFoundationHeight", heightMm);
            SetDoubleParameterByName(foundation, "IsolatedFoundationRotationAngle", rotation);
            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(foundation, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            var lateInts = new List<(string, int)>();
            if (shapeValue is int shapeCode)
            {
                lateInts.Add(("IsolatedFoundationShape", shapeCode));
            }

            var lateDoubles = new List<(string, double)>();
            if (widthTopMm is double topWidth)
            {
                lateDoubles.Add(("IsolatedFoundationWidthTop", topWidth));
            }

            if (depthTopMm is double topDepth)
            {
                lateDoubles.Add(("IsolatedFoundationDepthTop", topDepth));
            }

            ApplyLateParameters(project, foundation, lateInts, lateDoubles, materialId);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = foundation.Id,
                ["uniqueId"] = foundation.UniqueIdS,
                ["type"] = foundation.ObjectTypeS,
                ["levelId"] = level.Id,
                ["levelName"] = level.Name,
                ["x"] = x,
                ["y"] = y,
                ["width"] = widthMeters,
                ["depth"] = depthMeters,
                ["height"] = heightMeters,
                ["widthTop"] = widthTopMm is double wt ? Units.ToMeters(wt) : null,
                ["depthTop"] = depthTopMm is double dt ? Units.ToMeters(dt) : null,
                ["rotation"] = rotation,
                ["shape"] = shape,
                ["materialId"] = materialId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateRoom(
        int levelId,
        double x,
        double y,
        string? name,
        string? number,
        double? heightMeters,
        double? offsetMeters)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var heightMm = heightMeters is double requestedHeight
            ? Units.ToMillimeters(RequireFinite(requestedHeight, "height"))
            : (double?)null;
        if (heightMm is double height && height <= 0)
        {
            throw new RengaException("Room height must be positive.");
        }

        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = RengaIds.Room;
            args.HostObjectId = level.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = 0 },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var room = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the room. " + _connector.LastError);

            if (!string.IsNullOrWhiteSpace(name))
            {
                SetStringParameterByName(room, "RoomName", name.Trim());
            }

            if (!string.IsNullOrWhiteSpace(number))
            {
                SetStringParameterByName(room, "RoomNumber", number.Trim());
            }

            if (heightMm is double roomHeight)
            {
                SetDoubleParameterByName(room, "RoomHeight", roomHeight);
            }

            SetDoubleParameterByName(room, "Offset", offsetMm);

            op.Apply();
            applied = true;

            var regionInfo = DescribeRoomRegion(room, px, py);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = room.Id,
                ["uniqueId"] = room.UniqueIdS,
                ["type"] = room.ObjectTypeS,
                ["levelId"] = level.Id,
                ["levelName"] = level.Name,
                ["x"] = x,
                ["y"] = y,
                ["name"] = name,
                ["number"] = number,
                ["height"] = heightMeters,
                ["offset"] = offsetMeters,
                ["regionContourCount"] = regionInfo.ContourCount,
                ["regionOuterLength"] = regionInfo.OuterLength
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateStair(
        int levelId,
        double x1,
        double y1,
        double x2,
        double y2,
        double widthMeters,
        double heightMeters,
        double? thicknessMeters,
        int? numberOfSteps,
        string? shape,
        string? position,
        double? horizontalOffsetMeters,
        double? offsetMeters,
        int? materialId,
        string? mark)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var widthMm = Units.ToMillimeters(RequireFinite(widthMeters, "width"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Stair width and height must be positive.");
        }

        var thicknessMm = thicknessMeters is double requestedThickness
            ? Units.ToMillimeters(RequireFinite(requestedThickness, "thickness"))
            : (double?)null;
        if (thicknessMm is double thickness && thickness <= 0)
        {
            throw new RengaException("Stair thickness must be positive.");
        }

        if (numberOfSteps is int steps && steps <= 0)
        {
            throw new RengaException("Number of steps must be positive.");
        }

        var shapeValue = string.IsNullOrWhiteSpace(shape) ? (int?)null : ParseStairShape(shape);
        var positionValue = string.IsNullOrWhiteSpace(position) ? (int?)null : ParsePosition(position);
        var horizontalOffsetMm = Units.ToMillimeters(RequireFinite(horizontalOffsetMeters ?? 0, "horizontalOffset"));
        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));

        var project = _connector.RequireProject();
        var model = project.Model;
        ResolveLevel(model, levelId, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = CreateBaselineArgs(model, RengaIds.Stair, levelId, sx, sy, ex, ey, out var lengthMm);
            var stair = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the stair. " + _connector.LastError);
            if (stair is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created stair does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateLine2D(0, 0, lengthMm, 0));
            SetDoubleParameterByName(stair, "StairWidth", widthMm);
            SetDoubleParameterByName(stair, "StairHeight", heightMm);
            SetDoubleParameterByName(stair, "StairHorizontalOffset", horizontalOffsetMm);
            SetDoubleParameterByName(stair, "Offset", offsetMm);
            if (thicknessMm is double stairThickness)
            {
                SetDoubleParameterByName(stair, "StairThickness", stairThickness);
            }

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(stair, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            var lateInts = new List<(string, int)>();
            if (numberOfSteps is int stepCount)
            {
                lateInts.Add(("NumberOfSteps", stepCount));
            }

            if (shapeValue is int shapeCode)
            {
                lateInts.Add(("StairShape", shapeCode));
            }

            if (positionValue is int positionCode)
            {
                lateInts.Add(("StairPositionRelativeToBaseline", positionCode));
            }

            ApplyLateParameters(project, stair, lateInts, Array.Empty<(string, double)>(), materialId);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = stair.Id,
                ["uniqueId"] = stair.UniqueIdS,
                ["type"] = stair.ObjectTypeS,
                ["levelId"] = levelId,
                ["length"] = Units.ToMeters(lengthMm),
                ["width"] = widthMeters,
                ["height"] = heightMeters,
                ["thickness"] = thicknessMeters,
                ["numberOfSteps"] = numberOfSteps,
                ["shape"] = shape,
                ["position"] = position,
                ["materialId"] = materialId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateRailing(
        int levelId,
        double x1,
        double y1,
        double x2,
        double y2,
        double heightMeters,
        double? spaceBetweenBalustersMeters,
        double? offsetMeters,
        string? mark)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (heightMm <= 0)
        {
            throw new RengaException("Railing height must be positive.");
        }

        var spaceMm = spaceBetweenBalustersMeters is double requestedSpace
            ? Units.ToMillimeters(RequireFinite(requestedSpace, "spaceBetweenBalusters"))
            : (double?)null;
        if (spaceMm is double space && space <= 0)
        {
            throw new RengaException("Space between balusters must be positive.");
        }

        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));

        var project = _connector.RequireProject();
        var model = project.Model;
        ResolveLevel(model, levelId, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = CreateBaselineArgs(model, RengaIds.Railing, levelId, sx, sy, ex, ey, out var lengthMm);
            var railing = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the railing. " + _connector.LastError);
            if (railing is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created railing does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateLine2D(0, 0, lengthMm, 0));
            SetDoubleParameterByName(railing, "RailingHeight", heightMm);
            SetDoubleParameterByName(railing, "Offset", offsetMm);
            if (spaceMm is double balusterSpace)
            {
                SetDoubleParameterByName(railing, "SpaceBetweenBalusters", balusterSpace);
            }

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(railing, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = railing.Id,
                ["uniqueId"] = railing.UniqueIdS,
                ["type"] = railing.ObjectTypeS,
                ["levelId"] = levelId,
                ["length"] = Units.ToMeters(lengthMm),
                ["height"] = heightMeters,
                ["spaceBetweenBalusters"] = spaceBetweenBalustersMeters,
                ["offset"] = offsetMeters
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateRamp(
        int levelId,
        double x1,
        double y1,
        double x2,
        double y2,
        double widthMeters,
        double heightMeters,
        string? position,
        double? horizontalOffsetMeters,
        double? offsetMeters,
        int? materialId,
        string? mark)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var widthMm = Units.ToMillimeters(RequireFinite(widthMeters, "width"));
        var heightMm = Units.ToMillimeters(RequireFinite(heightMeters, "height"));
        if (widthMm <= 0 || heightMm <= 0)
        {
            throw new RengaException("Ramp width and height must be positive.");
        }

        var positionValue = string.IsNullOrWhiteSpace(position) ? (int?)null : ParsePosition(position);
        var horizontalOffsetMm = Units.ToMillimeters(RequireFinite(horizontalOffsetMeters ?? 0, "horizontalOffset"));
        var offsetMm = Units.ToMillimeters(RequireFinite(offsetMeters ?? 0, "offset"));

        var project = _connector.RequireProject();
        var model = project.Model;
        ResolveLevel(model, levelId, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = CreateBaselineArgs(model, RengaIds.Ramp, levelId, sx, sy, ex, ey, out var lengthMm);
            var ramp = model.CreateObject(args)
                ?? throw new RengaException("Renga did not create the ramp. " + _connector.LastError);
            if (ramp is not IBaseline2DObject baseline)
            {
                throw new RengaException("The created ramp does not expose IBaseline2DObject.");
            }

            baseline.SetBaseline(CreateLine2D(0, 0, lengthMm, 0));
            SetDoubleParameterByName(ramp, "RampWidth", widthMm);
            SetDoubleParameterByName(ramp, "RampHeight", heightMm);
            SetDoubleParameterByName(ramp, "RampHorizontalOffset", horizontalOffsetMm);
            SetDoubleParameterByName(ramp, "Offset", offsetMm);
            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(ramp, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            var lateInts = new List<(string, int)>();
            if (positionValue is int positionCode)
            {
                lateInts.Add(("RampPositionRelativeToBaseline", positionCode));
            }

            ApplyLateParameters(project, ramp, lateInts, Array.Empty<(string, double)>(), materialId);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = ramp.Id,
                ["uniqueId"] = ramp.UniqueIdS,
                ["type"] = ramp.ObjectTypeS,
                ["levelId"] = levelId,
                ["length"] = Units.ToMeters(lengthMm),
                ["width"] = widthMeters,
                ["height"] = heightMeters,
                ["position"] = position,
                ["materialId"] = materialId
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object ListDrawings()
    {
        var project = _connector.RequireProject();
        var items = DescribeDrawings(project);
        return new Dictionary<string, object?>
        {
            ["count"] = items.Length,
            ["drawings"] = items
        };
    }

    public object GetDrawingTitleBlocks(string drawingId)
    {
        var project = _connector.RequireProject();
        var drawing = FindLegacyDrawing(project, drawingId)
            ?? throw new RengaException(
                $"Drawing {drawingId} was not found. Use renga_list_drawings to inspect drawings.");

        var blocks = new List<Dictionary<string, object?>>();
        var count = drawing.TitleBlockInstanceCount;
        for (var i = 0; i < count; i++)
        {
            var titleBlock = drawing.GetTitleBlockInstance(i);
            var rows = titleBlock.RowCount;
            var columns = titleBlock.ColumnCount;
            var cells = new string[rows][];
            for (var row = 0; row < rows; row++)
            {
                cells[row] = new string[columns];
                for (var column = 0; column < columns; column++)
                {
                    cells[row][column] = titleBlock.GetCellValue(column, row);
                }
            }

            blocks.Add(new Dictionary<string, object?>
            {
                ["index"] = i,
                ["name"] = titleBlock.Name,
                ["rows"] = rows,
                ["columns"] = columns,
                ["cells"] = cells
            });
        }

        return new Dictionary<string, object?>
        {
            ["drawingId"] = drawingId,
            ["name"] = drawing.Name,
            ["count"] = count,
            ["titleBlocks"] = blocks.ToArray()
        };
    }

    public object CreateDrawing(string name, string? pageFormat, string? orientation, string? layoutStyle)
    {
        name = RequireName(name, "name");
        var orientationValue = string.IsNullOrWhiteSpace(orientation)
            ? (int?)null
            : ParsePageOrientation(orientation);

        var project = _connector.RequireProject();
        var pageFormatId = string.IsNullOrWhiteSpace(pageFormat)
            ? (int?)null
            : ResolveEntityReference(project.PageFormatStyles, pageFormat, "page format").Id;
        var layoutStyleId = string.IsNullOrWhiteSpace(layoutStyle)
            ? (int?)null
            : ResolveEntityReference(project.LayoutStyles, layoutStyle, "layout style").Id;

        var op = StartOperation(project);
        IEntity drawing;
        var applied = false;
        try
        {
            var args = project.Drawings2.CreateNewEntityArgs();
            args.TypeId = RengaIds.Drawing;
            drawing = project.Drawings2.Create(args)
                ?? throw new RengaException("Renga did not create the drawing. " + _connector.LastError);
            SetContainerString(GetEntityContainer(drawing), RengaParams.EntityName, name);
            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        if (pageFormatId is not null || layoutStyleId is not null || orientationValue is not null)
        {
            var settingsOp = StartOperation(project);
            var settingsApplied = false;
            try
            {
                var container = GetEntityContainer(drawing);
                if (pageFormatId is int format) SetContainerIntByName(container, "PageFormatId", format);
                if (layoutStyleId is int layout) SetContainerIntByName(container, "LayoutStyleId", layout);
                if (orientationValue is int side) SetContainerIntByName(container, "PageOrientation", side);
                settingsOp.Apply();
                settingsApplied = true;
            }
            catch (Exception ex)
            {
                if (!settingsApplied)
                {
                    TryRollback(settingsOp);
                }

                throw new RengaException(
                    $"Drawing {drawing.Id} was created, but page settings could not be applied: {ex.Message}",
                    ex);
            }
        }

        return DescribeDrawing(project, drawing);
    }

    public object RenameDrawing(string drawingId, string name)
    {
        name = RequireName(name, "name");
        var project = _connector.RequireProject();
        var drawing = RequireDrawingEntity(project, drawingId);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            SetContainerString(GetEntityContainer(drawing), RengaParams.EntityName, name);
            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        return DescribeDrawing(project, RequireDrawingEntity(project, drawingId));
    }

    public object DeleteDrawing(string drawingId)
    {
        var project = _connector.RequireProject();
        var drawing = RequireDrawingEntity(project, drawingId);
        var entityId = drawing.Id;

        var op = StartOperation(project);
        var applied = false;
        try
        {
            project.Drawings2.RemoveByUniqueIdS(drawingId);
            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        return new Dictionary<string, object?>
        {
            ["deleted"] = true,
            ["drawingId"] = drawingId,
            ["entityId"] = entityId,
            ["remaining"] = project.Drawings2.Count
        };
    }

    public object ExportDrawing(string drawingId, string path, string? format, string? cadVersion)
    {
        var project = _connector.RequireProject();
        var normalizedFormat = NormalizeDrawingFormat(format);
        var extension = DrawingFormatExtension(normalizedFormat);
        var fullPath = EnsureOutputPath(path, extension);
        var drawing = FindLegacyDrawing(project, drawingId)
            ?? throw new RengaException(
                $"Drawing {drawingId} was not found. Use renga_list_drawings to inspect drawings.");
        var version = ParseAutocadVersion(cadVersion);
        var result = ExportDrawingFile(drawing, fullPath, normalizedFormat, version);
        _connector.Check(result, $"ExportTo{normalizedFormat.ToUpperInvariant()}");

        return new Dictionary<string, object?>
        {
            ["exported"] = true,
            ["drawingId"] = drawingId,
            ["name"] = drawing.Name,
            ["format"] = normalizedFormat,
            ["path"] = fullPath,
            ["bytes"] = new FileInfo(fullPath).Length
        };
    }

    public object ExportAllDrawings(string directory, string? format, string? cadVersion)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new RengaException("Directory must be non-empty.");
        }

        var project = _connector.RequireProject();
        var normalizedFormat = NormalizeDrawingFormat(format);
        var extension = DrawingFormatExtension(normalizedFormat);
        var version = ParseAutocadVersion(cadVersion);
        var fullDirectory = Path.GetFullPath(directory);
        Directory.CreateDirectory(fullDirectory);

        var items = new List<Dictionary<string, object?>>();
        var exported = 0;
        for (var i = 0; i < project.Drawings2.Count; i++)
        {
            var entity = project.Drawings2.GetByIndex(i);
            var drawingId = entity.UniqueIdS;
            var name = GetStringParameter(GetEntityContainer(entity), "EntityName") ?? entity.Name;
            var drawing = FindLegacyDrawing(project, drawingId);
            if (drawing is null)
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["index"] = i,
                    ["drawingId"] = drawingId,
                    ["name"] = name,
                    ["error"] = "Drawing is not available through the legacy drawing collection."
                });
                continue;
            }

            var fileName = $"{i:00}-{SanitizeFileName(name)}.{extension}";
            var fullPath = Path.Combine(fullDirectory, fileName);
            try
            {
                var result = ExportDrawingFile(drawing, fullPath, normalizedFormat, version);
                if (result != 0)
                {
                    items.Add(new Dictionary<string, object?>
                    {
                        ["index"] = i,
                        ["drawingId"] = drawingId,
                        ["name"] = name,
                        ["error"] = $"Export failed with code {result}. {_connector.LastError}"
                    });
                    continue;
                }

                exported++;
                items.Add(new Dictionary<string, object?>
                {
                    ["index"] = i,
                    ["drawingId"] = drawingId,
                    ["name"] = name,
                    ["path"] = fullPath,
                    ["bytes"] = new FileInfo(fullPath).Length
                });
            }
            catch (Exception ex)
            {
                items.Add(new Dictionary<string, object?>
                {
                    ["index"] = i,
                    ["drawingId"] = drawingId,
                    ["name"] = name,
                    ["error"] = ex.Message
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["directory"] = fullDirectory,
            ["format"] = normalizedFormat,
            ["exported"] = exported,
            ["failed"] = project.Drawings2.Count - exported,
            ["drawings"] = items.ToArray()
        };
    }

    public object ListDrawingResources()
    {
        var project = _connector.RequireProject();

        var pageFormats = new List<Dictionary<string, object?>>();
        for (var i = 0; i < project.PageFormatStyles.Count; i++)
        {
            var entity = project.PageFormatStyles.GetByIndex(i);
            var container = GetEntityContainer(entity);
            var widthMm = GetDoubleParameter(container, "PageFormatWidth");
            var heightMm = GetDoubleParameter(container, "PageFormatHeight");
            pageFormats.Add(new Dictionary<string, object?>
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name,
                ["widthMm"] = widthMm,
                ["heightMm"] = heightMm,
                ["width"] = widthMm is double width ? width / Units.MillimetersPerMeter : null,
                ["height"] = heightMm is double height ? height / Units.MillimetersPerMeter : null,
                ["referenceName"] = GetStringParameter(container, "PageFormatReferenceName")
            });
        }

        return new Dictionary<string, object?>
        {
            ["pageFormats"] = pageFormats.ToArray(),
            ["layoutStyles"] = DescribeEntities(project.LayoutStyles),
            ["scales"] = DescribeEntities(project.Scales),
            ["legendStyles"] = DescribeEntities(project.LegendStyles)
        };
    }

    public object ListMepCategories()
    {
        var project = _connector.RequireProject();
        return new Dictionary<string, object?>
        {
            ["systemCategories"] = MepSystemCategories
                .Select(item => new Dictionary<string, object?>
                {
                    ["name"] = item.Name,
                    ["value"] = (int)item.Category,
                    ["kind"] = item.Kind
                })
                .ToArray(),
            ["styleCategories"] = new Dictionary<string, object?>
            {
                ["equipment"] = DescribeEntities(project.EquipmentCategories),
                ["mechanical_equipment"] = DescribeEntities(project.MechanicalEquipmentCategories),
                ["plumbing_fixture"] = DescribeEntities(project.PlumbingFixtureCategories),
                ["lighting_fixture"] = DescribeEntities(project.LightingFixtureCategories),
                ["wiring_accessory"] = DescribeEntities(project.WiringAccessoryCategories),
                ["electric_distribution_board"] = DescribeEntities(project.ElectricDistributionBoardCategories),
                ["pipe_fitting"] = DescribeEntities(project.PipeFittingCategories),
                ["pipe_accessory"] = DescribeEntities(project.PipeAccessoryCategories),
                ["duct_fitting"] = DescribeEntities(project.DuctFittingCategories),
                ["duct_accessory"] = DescribeEntities(project.DuctAccessoryCategories)
            }
        };
    }

    public object CreateRoutePoint(int levelId, double x, double y, double z, string? name)
    {
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var pz = Units.ToMillimeters(RequireFinite(z, "z"));

        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, null);
        var localZ = pz - level.ElevationMm;
        var pointName = string.IsNullOrWhiteSpace(name) ? "Route point" : name.Trim();

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var point = CreateRoutePointObject(model, level.Id, px, py, localZ, pointName);
            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = point.Id,
                ["uniqueId"] = point.UniqueIdS,
                ["type"] = point.ObjectTypeS,
                ["name"] = pointName,
                ["levelId"] = level.Id,
                ["levelName"] = level.Name,
                ["x"] = x,
                ["y"] = y,
                ["z"] = z,
                ["ports"] = GetPortCount(point)
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreatePipe(
        int levelId,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        int? styleId,
        int? insulationId,
        IReadOnlyList<int>? fittingStyleIds,
        string? systemCategory,
        string? mark) => CreateMepLine(
            "pipe", levelId, x1, y1, z1, x2, y2, z2, styleId, insulationId, fittingStyleIds, systemCategory, mark);

    public object CreateDuct(
        int levelId,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        int? styleId,
        int? insulationId,
        IReadOnlyList<int>? fittingStyleIds,
        string? systemCategory,
        string? mark) => CreateMepLine(
            "duct", levelId, x1, y1, z1, x2, y2, z2, styleId, insulationId, fittingStyleIds, systemCategory, mark);

    public object CreateElectricalLine(
        int levelId,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        int? styleId,
        string? systemCategory,
        string? mark) => CreateMepLine(
            "electrical", levelId, x1, y1, z1, x2, y2, z2, styleId, null, null, systemCategory, mark);

    public object ConnectMep(
        string kind,
        int sourceId,
        int sourcePort,
        int targetId,
        int targetPort,
        int? styleId,
        int? insulationId,
        IReadOnlyList<int>? fittingStyleIds,
        string? systemCategory,
        string? mark)
    {
        var info = ResolveMepLineKind(kind);
        var project = _connector.RequireProject();
        var model = project.Model;
        var style = ResolveMepStyle(project, info.Styles(project), styleId, info.Kind);
        var category = ResolveMepSystemCategory(systemCategory, info.Kind);
        var insulation = insulationId ?? 0;
        var fittings = fittingStyleIds ?? Array.Empty<int>();

        if (sourcePort < -1 || targetPort < -1)
        {
            throw new RengaException("Port indices must be zero-based; use -1 for a route endpoint.");
        }

        var (route, line) = ConnectMepCore(
            project,
            model,
            info,
            sourceId,
            sourcePort,
            targetId,
            targetPort,
            style.Id,
            insulation,
            fittings,
            category);

        ApplyMark(project, line, mark);
        var routeInfo = DescribeRoute(route, project);

        return new Dictionary<string, object?>
        {
            ["created"] = true,
            ["kind"] = info.Kind,
            ["routeId"] = route.Id,
            ["objectId"] = line.Id,
            ["objectUniqueId"] = line.UniqueIdS,
            ["objectName"] = line.Name,
            ["styleId"] = style.Id,
            ["styleName"] = style.Name,
            ["systemCategory"] = category.ToString(),
            ["systemCategoryValue"] = (int)category,
            ["sourceId"] = sourceId,
            ["sourcePort"] = sourcePort,
            ["targetId"] = targetId,
            ["targetPort"] = targetPort,
            ["length"] = routeInfo.Length,
            ["segments"] = routeInfo.Segments,
            ["start"] = routeInfo.Start,
            ["end"] = routeInfo.End
        };
    }

    public object CreateMepEquipment(
        string kind,
        int? levelId,
        int? hostId,
        double x,
        double y,
        double z,
        int? styleId,
        double? rotationDegrees,
        string? mark)
    {
        var info = ResolveMepEquipmentKind(kind);
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var pz = Units.ToMillimeters(RequireFinite(z, "z"));
        var rotation = RequireFinite(rotationDegrees ?? 0, "rotation");

        var project = _connector.RequireProject();
        var model = project.Model;
        var style = ResolveMepStyle(project, info.Styles(project), styleId, info.Kind);

        IModelObject host;
        IModelObject levelObject;
        if (info.RequiresHost)
        {
            var resolvedHostId = hostId
                ?? throw new RengaException($"{info.Kind} requires hostId (wall, column, beam, or floor).");
            host = FindObject(model, resolvedHostId)
                ?? throw new RengaException($"Host object with ID {resolvedHostId} was not found.");
            var hostLevelId = (host as ILevelObject)?.LevelId
                ?? throw new RengaException($"Host {resolvedHostId} does not expose ILevelObject.");
            levelObject = FindLevel(model, hostLevelId)
                ?? throw new RengaException($"Level with ID {hostLevelId} was not found.");
        }
        else
        {
            var resolvedLevelId = levelId
                ?? throw new RengaException($"{info.Kind} requires levelId.");
            levelObject = FindLevel(model, resolvedLevelId)
                ?? throw new RengaException($"Level with ID {resolvedLevelId} was not found.");
            host = levelObject;
        }

        var level = (ILevel)levelObject;
        var localZ = info.RequiresHost ? pz : pz - level.Elevation;
        var angleRadians = rotation * Math.PI / 180.0;
        var axisX = info.RequiresHost
            ? new Vector3D { X = Math.Cos(angleRadians), Y = Math.Sin(angleRadians), Z = 0 }
            : new Vector3D { X = 1, Y = 0, Z = 0 };

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = info.TypeId;
            args.HostObjectId = host.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = localZ },
                xAxis = axisX,
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };
            args.StyleId = style.Id;

            var obj = model.CreateObject(args)
                ?? throw new RengaException($"Renga did not create {info.Kind}. " + _connector.LastError);

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(obj, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            if (!info.RequiresHost && Math.Abs(rotation) > 1e-9)
            {
                ApplyLateParameters(
                    project,
                    obj,
                    Array.Empty<(string, int)>(),
                    new[] { ("PrecessionAngle", rotation) },
                    null);
            }

            var levelObjectInfo = obj as ILevelObject;
            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["kind"] = info.Kind,
                ["id"] = obj.Id,
                ["uniqueId"] = obj.UniqueIdS,
                ["type"] = obj.ObjectTypeS,
                ["name"] = obj.Name,
                ["levelId"] = levelObjectInfo?.LevelId ?? levelObject.Id,
                ["levelName"] = level.LevelName,
                ["hostId"] = host.Id,
                ["x"] = x,
                ["y"] = y,
                ["z"] = z,
                ["rotation"] = rotation,
                ["styleId"] = style.Id,
                ["styleName"] = style.Name,
                ["category"] = GetStringParameter(obj.GetParameters(), "Category"),
                ["ports"] = GetPortCount(obj)
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object PlaceMepComponent(
        string kind,
        int routeId,
        double x,
        double y,
        double z,
        int? styleId,
        double? angleDegrees,
        string? mark)
    {
        var info = ResolveMepComponentKind(kind);
        var px = Units.ToMillimeters(RequireFinite(x, "x"));
        var py = Units.ToMillimeters(RequireFinite(y, "y"));
        var pz = Units.ToMillimeters(RequireFinite(z, "z"));
        var angle = RequireFinite(angleDegrees ?? 0, "angle");

        var project = _connector.RequireProject();
        var model = project.Model;
        var route = FindObject(model, routeId)
            ?? throw new RengaException($"Route with ID {routeId} was not found.");
        if (route.ObjectType != RengaIds.Route)
        {
            throw new RengaException($"Object {routeId} is not a route.");
        }

        var style = ResolveMepStyle(project, info.Styles(project), styleId, info.Kind);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = model.CreateNewEntityArgs();
            args.TypeId = info.TypeId;
            args.HostObjectId = routeId;
            args.StyleId = style.Id;
            args.Placement3D = new Placement3D
            {
                Origin = new Point3D { X = px, Y = py, Z = pz },
                xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
                zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
            };

            var obj = model.CreateObject(args)
                ?? throw new RengaException($"Renga did not create {info.Kind}. " + _connector.LastError);

            if (!string.IsNullOrWhiteSpace(mark))
            {
                SetStringParameterByName(obj, "Mark", mark.Trim());
            }

            op.Apply();
            applied = true;

            if (Math.Abs(angle) > 1e-9)
            {
                ApplyLateParameters(
                    project,
                    obj,
                    Array.Empty<(string, int)>(),
                    new[] { (info.AngleParameter, angle) },
                    null);
            }

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["kind"] = info.Kind,
                ["id"] = obj.Id,
                ["uniqueId"] = obj.UniqueIdS,
                ["type"] = obj.ObjectTypeS,
                ["name"] = obj.Name,
                ["routeId"] = routeId,
                ["x"] = x,
                ["y"] = y,
                ["z"] = z,
                ["angle"] = angle,
                ["styleId"] = style.Id,
                ["styleName"] = style.Name,
                ["category"] = GetStringParameter(obj.GetParameters(), "Category")
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object ListMepRoutes(int? limit)
    {
        var project = _connector.RequireProject();
        var model = project.Model;
        var max = Math.Clamp(limit ?? 500, 1, 5000);
        var items = new List<Dictionary<string, object?>>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count && items.Count < max; i++)
        {
            var obj = objects.GetByIndex(i);
            if (obj.ObjectType != RengaIds.Route)
            {
                continue;
            }

            var info = DescribeRoute(obj, project);
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = obj.Id,
                ["uniqueId"] = obj.UniqueIdS,
                ["name"] = obj.Name,
                ["styleId"] = info.StyleId,
                ["styleName"] = info.StyleName,
                ["sourceId"] = info.SourceId,
                ["sourcePort"] = info.SourcePort,
                ["targetId"] = info.TargetId,
                ["targetPort"] = info.TargetPort,
                ["segments"] = info.Segments,
                ["joints"] = info.Joints,
                ["length"] = info.Length,
                ["start"] = info.Start,
                ["end"] = info.End,
                ["objectOnRouteIds"] = info.ObjectOnRouteIds
            });
        }

        return new Dictionary<string, object?>
        {
            ["count"] = items.Count,
            ["limit"] = max,
            ["routes"] = items.ToArray()
        };
    }

    public object CreateMepStyle(
        string kind,
        string name,
        int? materialId,
        int? categoryId,
        double? diameterMeters,
        double? insideDiameterMeters,
        double? wallThicknessMeters,
        double? widthMeters,
        double? heightMeters,
        string? profile,
        int? conductorNumber,
        int? conductorStyleId)
    {
        name = RequireName(name, "name");
        var info = ResolveMepStyleKind(kind);
        var project = _connector.RequireProject();
        var collection = info.Styles(project);

        int? resolvedCategoryId = null;
        if (info.Categories is not null)
        {
            var requiredCategoryId = categoryId
                ?? throw new RengaException($"{info.Kind} style requires categoryId. Use renga_list_mep_categories.");
            var category = info.Categories(project).GetById(requiredCategoryId)
                ?? throw new RengaException($"Category {requiredCategoryId} was not found for {info.Kind}.");
            resolvedCategoryId = category.Id;
        }

        var diameterMm = diameterMeters is double diameter
            ? Units.ToMillimeters(RequireFinite(diameter, "diameter"))
            : (double?)null;
        var insideDiameterMm = insideDiameterMeters is double inside
            ? Units.ToMillimeters(RequireFinite(inside, "insideDiameter"))
            : (double?)null;
        var wallThicknessMm = wallThicknessMeters is double wall
            ? Units.ToMillimeters(RequireFinite(wall, "wallThickness"))
            : (double?)null;
        var widthMm = widthMeters is double widthValue
            ? Units.ToMillimeters(RequireFinite(widthValue, "width"))
            : (double?)null;
        var heightMm = heightMeters is double heightValue
            ? Units.ToMillimeters(RequireFinite(heightValue, "height"))
            : (double?)null;
        var profileValue = string.IsNullOrWhiteSpace(profile) ? (int?)null : ParseDuctProfile(profile);
        if (profileValue is null && (widthMm is not null || heightMm is not null))
        {
            profileValue = 1;
        }

        var op = StartOperation(project);
        var applied = false;
        IEntity entity;
        try
        {
            var args = collection.CreateNewEntityArgs();
            args.TypeId = info.TypeId;
            if (resolvedCategoryId is int category) args.CategoryId = category;
            entity = collection.Create(args)
                ?? throw new RengaException($"Renga did not create the {info.Kind} style. " + _connector.LastError);

            var container = GetEntityContainer(entity);
            SetContainerString(container, RengaParams.EntityName, name);
            if (materialId is int material)
            {
                SetContainerInt(container, RengaParams.MaterialId, material);
            }

            if (info.Kind == "pipe")
            {
                if (diameterMm is double outside) SetContainerDoubleByName(container, "PipeOutsideDiameter", outside);
                if (wallThicknessMm is double pipeWall) SetContainerDoubleByName(container, "PipeWallThickness", pipeWall);
                if (insideDiameterMm is double pipeInside) SetContainerDoubleByName(container, "PipeInsideDiameter", pipeInside);
            }
            else if (info.Kind == "duct")
            {
                if (wallThicknessMm is double ductWall) SetContainerDoubleByName(container, "DuctWallThickness", ductWall);
            }

            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        var lateInts = new List<(string, int)>();
        var lateDoubles = new List<(string, double)>();
        if (info.Kind == "electrical_circuit_line")
        {
            if (conductorNumber is int number) lateInts.Add(("ElectricalConductorNumber", number));
            if (conductorStyleId is int conductorStyle) lateInts.Add(("ConductorStyleId", conductorStyle));
        }
        else if (info.Kind == "duct")
        {
            if (profileValue is int ductProfile) lateInts.Add(("DuctProfile", ductProfile));
            if (widthMm is double ductWidth) lateDoubles.Add(("DuctWidth", ductWidth));
            if (heightMm is double ductHeight) lateDoubles.Add(("DuctHeight", ductHeight));
        }

        var containerAfter = GetEntityContainer(entity);
        ApplyLateContainerParameters(
            project,
            containerAfter,
            $"Style {entity.Id}",
            lateInts,
            lateDoubles,
            null);

        return new Dictionary<string, object?>
        {
            ["created"] = true,
            ["kind"] = info.Kind,
            ["id"] = entity.Id,
            ["uniqueId"] = entity.UniqueIdS,
            ["name"] = name,
            ["typeId"] = entity.TypeIdS,
            ["categoryId"] = resolvedCategoryId,
            ["materialId"] = materialId
        };
    }

    private object CreateMepLine(
        string kind,
        int levelId,
        double x1,
        double y1,
        double z1,
        double x2,
        double y2,
        double z2,
        int? styleId,
        int? insulationId,
        IReadOnlyList<int>? fittingStyleIds,
        string? systemCategory,
        string? mark)
    {
        var sx = Units.ToMillimeters(RequireFinite(x1, "x1"));
        var sy = Units.ToMillimeters(RequireFinite(y1, "y1"));
        var sz = Units.ToMillimeters(RequireFinite(z1, "z1"));
        var ex = Units.ToMillimeters(RequireFinite(x2, "x2"));
        var ey = Units.ToMillimeters(RequireFinite(y2, "y2"));
        var ez = Units.ToMillimeters(RequireFinite(z2, "z2"));
        if (Math.Abs(sx - ex) < 1e-6 && Math.Abs(sy - ey) < 1e-6 && Math.Abs(sz - ez) < 1e-6)
        {
            throw new RengaException("MEP line start and end points must be different.");
        }

        var info = ResolveMepLineKind(kind);
        var project = _connector.RequireProject();
        var model = project.Model;
        var level = ResolveLevel(model, levelId, null);
        var style = ResolveMepStyle(project, info.Styles(project), styleId, info.Kind);
        var category = ResolveMepSystemCategory(systemCategory, info.Kind);
        var insulation = insulationId ?? 0;
        var fittings = fittingStyleIds ?? Array.Empty<int>();

        var startLocalZ = sz - level.ElevationMm;
        var endLocalZ = ez - level.ElevationMm;

        int startId;
        int endId;
        var pointsOp = StartOperation(project);
        try
        {
            var start = CreateRoutePointObject(model, level.Id, sx, sy, startLocalZ, $"{info.DisplayName} start");
            var end = CreateRoutePointObject(model, level.Id, ex, ey, endLocalZ, $"{info.DisplayName} end");
            pointsOp.Apply();
            startId = start.Id;
            endId = end.Id;
        }
        catch
        {
            TryRollback(pointsOp);
            throw;
        }

        try
        {
            var (route, line) = ConnectMepCore(
                project,
                model,
                info,
                startId,
                0,
                endId,
                0,
                style.Id,
                insulation,
                fittings,
                category);

            ApplyMark(project, line, mark);
            var routeInfo = DescribeRoute(route, project);

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["kind"] = info.Kind,
                ["routeId"] = route.Id,
                ["objectId"] = line.Id,
                ["objectUniqueId"] = line.UniqueIdS,
                ["objectName"] = line.Name,
                ["routePointIds"] = new[] { startId, endId },
                ["styleId"] = style.Id,
                ["styleName"] = style.Name,
                ["systemCategory"] = category.ToString(),
                ["systemCategoryValue"] = (int)category,
                ["length"] = routeInfo.Length,
                ["segments"] = routeInfo.Segments,
                ["start"] = new { x = x1, y = y1, z = z1 },
                ["end"] = new { x = x2, y = y2, z = z2 }
            };
        }
        catch
        {
            TryDeleteObjects(project, model, startId, endId);
            throw;
        }
    }

    private (IModelObject Route, IModelObject Line) ConnectMepCore(
        IProject project,
        IModel model,
        MepLineKind info,
        int sourceId,
        int sourcePort,
        int targetId,
        int targetPort,
        int styleId,
        int insulationId,
        IReadOnlyList<int> fittingStyleIds,
        SystemCategory category)
    {
        var before = SnapshotObjectIds(model);
        var connector = project.EngineeringObjectConnector;
        _connector.ClearLastError();

        var op = StartOperation(project);
        var applied = false;
        try
        {
            switch (info.Kind)
            {
                case "pipe":
                {
                    var parameters = connector.CreatePipeSystemConnectionParameters();
                    parameters.AddPipeStyleMagistral(styleId, insulationId);
                    parameters.AddPipeStyleBranch(styleId, insulationId);
                    foreach (var fitting in fittingStyleIds)
                    {
                        parameters.AddPipeFittingStyle(fitting);
                    }

                    connector.CreatePipeSystemConnection(
                        parameters, sourceId, sourcePort, targetId, targetPort, category);
                    break;
                }
                case "duct":
                {
                    var parameters = connector.CreateDuctSystemConnectionParameters();
                    parameters.AddDuctStyleMagistral(styleId, insulationId);
                    parameters.AddDuctStyleBranch(styleId, insulationId);
                    foreach (var fitting in fittingStyleIds)
                    {
                        parameters.AddDuctFittingStyle(fitting);
                    }

                    connector.CreateDuctSystemConnection(
                        parameters, sourceId, sourcePort, targetId, targetPort, category);
                    break;
                }
                case "electrical":
                {
                    var parameters = connector.CreateElectricalSystemConnectionParameters();
                    parameters.AddElectricalCircuitLineStyle(styleId);
                    connector.CreateElectricalSystemConnection(
                        parameters, sourceId, sourcePort, targetId, targetPort, category);
                    break;
                }
                default:
                    throw new RengaException($"Unsupported MEP kind '{info.Kind}'.");
            }

            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        IModelObject? route = null;
        IModelObject? line = null;
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (before.Contains(obj.Id))
            {
                continue;
            }

            if (obj.ObjectType == RengaIds.Route && route is null)
            {
                route = obj;
            }
            else if (obj.ObjectType == info.TypeId && line is null)
            {
                line = obj;
            }
        }

        if (route is null || line is null)
        {
            throw new RengaException(
                $"Renga did not create a {info.Kind} route. Check style compatibility, system category, " +
                $"and port indices. {_connector.LastError}".Trim());
        }

        return (route, line);
    }

    private static IModelObject CreateRoutePointObject(
        IModel model,
        int levelId,
        double xMm,
        double yMm,
        double zLocalMm,
        string name)
    {
        var args = model.CreateNewEntityArgs();
        args.TypeId = RengaIds.RoutePoint;
        args.HostObjectId = levelId;
        args.Placement3D = new Placement3D
        {
            Origin = new Point3D { X = xMm, Y = yMm, Z = zLocalMm },
            xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
            zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
        };

        var point = model.CreateObject(args)
            ?? throw new RengaException("Renga did not create the route point.");
        SetStringParameterByName(point, "RoutePointName", name);
        return point;
    }

    private void ApplyMark(IProject project, IModelObject obj, string? mark)
    {
        if (string.IsNullOrWhiteSpace(mark))
        {
            return;
        }

        var op = StartOperation(project);
        var applied = false;
        try
        {
            SetStringParameterByName(obj, "Mark", mark.Trim());
            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw new RengaException($"Object {obj.Id} was created, but Mark could not be set.", new Exception());
        }
    }

    private void TryDeleteObjects(IProject project, IModel model, params int[] ids)
    {
        if (ids.Length == 0)
        {
            return;
        }

        try
        {
            var op = StartOperation(project);
            foreach (var id in ids)
            {
                try
                {
                    model.DeleteObjectById(id);
                }
                catch
                {
                    // ignored
                }
            }

            op.Apply();
        }
        catch
        {
            // ignored
        }
    }

    private static HashSet<int> SnapshotObjectIds(IModel model)
    {
        var result = new HashSet<int>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            result.Add(objects.GetByIndex(i).Id);
        }

        return result;
    }

    private static int GetPortCount(IModelObject obj)
    {
        try
        {
            return (obj.GetInterfaceByName("IEntityWithPorts") as IEntityWithPorts)?.Count ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    private static RouteInfo DescribeRoute(IModelObject route, IProject project)
    {
        var info = new RouteInfo();
        try
        {
            var routeParams = route.GetInterfaceByName("IRouteParams") as IRouteParams;
            if (routeParams is null)
            {
                return info;
            }

            info.SourceId = routeParams.SourceModelObjectId;
            info.SourcePort = routeParams.SourcePortIndex;
            info.TargetId = routeParams.TargetModelObjectId;
            info.TargetPort = routeParams.TargetPortIndex;
            info.StyleId = routeParams.SystemStyleId;
            info.StyleName = FindEntityName(project.SystemStyles, info.StyleId);
            info.Segments = routeParams.GetFlowSegmentCount();
            info.Joints = routeParams.GetJointCount();

            var onRoute = new List<int>();
            var onRouteCount = routeParams.GetObjectOnRouteCount();
            for (var i = 0; i < onRouteCount; i++)
            {
                try
                {
                    onRoute.Add(routeParams.GetObjectOnRoutePlacement(i).Id);
                }
                catch
                {
                    // ignored
                }
            }

            info.ObjectOnRouteIds = onRoute.ToArray();

            var contour = routeParams.GetContour();
            if (contour is not null)
            {
                var begin = contour.GetBeginPoint();
                var end = contour.GetEndPoint();
                info.Length = Units.ToMeters(contour.GetLength());
                info.Start = new { x = Units.ToMeters(begin.X), y = Units.ToMeters(begin.Y), z = Units.ToMeters(begin.Z) };
                info.End = new { x = Units.ToMeters(end.X), y = Units.ToMeters(end.Y), z = Units.ToMeters(end.Z) };
            }
        }
        catch
        {
            // ignored
        }

        return info;
    }

    private static MepLineKind ResolveMepLineKind(string kind)
    {
        var normalized = NormalizeMepKind(kind);
        return normalized switch
        {
            "pipe" => new MepLineKind(
                "pipe",
                "Pipe",
                RengaIds.Pipe,
                project => project.PipeStyles,
                SystemCategory.SystemCategory_DomesticColdWater),
            "duct" => new MepLineKind(
                "duct",
                "Duct",
                RengaIds.Duct,
                project => project.DuctStyles,
                SystemCategory.SystemCategory_Ventilation),
            "electrical" or "electrical_circuit_line" or "electrical_line" => new MepLineKind(
                "electrical",
                "Electrical circuit line",
                RengaIds.ElectricalCircuitLine,
                project => project.ElectricalCircuitLineStyles,
                SystemCategory.SystemCategory_LightingCircuit),
            _ => throw new RengaException("MEP line kind must be pipe, duct, or electrical.")
        };
    }

    private static MepEquipmentKind ResolveMepEquipmentKind(string kind)
    {
        var normalized = NormalizeMepKind(kind);
        return normalized switch
        {
            "equipment" => new MepEquipmentKind(
                "equipment", RengaIds.Equipment, project => project.EquipmentStyles, false),
            "mechanical_equipment" => new MepEquipmentKind(
                "mechanical_equipment", RengaIds.MechanicalEquipment,
                project => project.MechanicalEquipmentStyles, false),
            "plumbing_fixture" => new MepEquipmentKind(
                "plumbing_fixture", RengaIds.PlumbingFixture,
                project => project.PlumbingFixtureStyles, false),
            "lighting_fixture" => new MepEquipmentKind(
                "lighting_fixture", RengaIds.LightingFixture,
                project => project.LightingFixtureStyles, true),
            "wiring_accessory" => new MepEquipmentKind(
                "wiring_accessory", RengaIds.WiringAccessory,
                project => project.WiringAccessoryStyles, true),
            "electric_distribution_board" or "distribution_board" => new MepEquipmentKind(
                "electric_distribution_board", RengaIds.ElectricDistributionBoard,
                project => project.ElectricDistributionBoardStyles, true),
            _ => throw new RengaException(
                "MEP equipment kind must be equipment, mechanical_equipment, plumbing_fixture, " +
                "lighting_fixture, wiring_accessory, or electric_distribution_board.")
        };
    }

    private static MepComponentKind ResolveMepComponentKind(string kind)
    {
        var normalized = NormalizeMepKind(kind);
        return normalized switch
        {
            "pipe_fitting" => new MepComponentKind(
                "pipe_fitting", RengaIds.PipeFitting, project => project.PipeFittingStyles,
                "PipeFittingRotationAngle"),
            "pipe_accessory" => new MepComponentKind(
                "pipe_accessory", RengaIds.PipeAccessory, project => project.PipeAccessoryStyles,
                "PipeAccessoryAngle"),
            "duct_fitting" => new MepComponentKind(
                "duct_fitting", RengaIds.DuctFitting, project => project.DuctFittingStyles,
                "DuctFittingRotationAngle"),
            "duct_accessory" => new MepComponentKind(
                "duct_accessory", RengaIds.DuctAccessory, project => project.DuctAccessoryStyles,
                "DuctAccessoryAngle"),
            _ => throw new RengaException(
                "MEP component kind must be pipe_fitting, pipe_accessory, duct_fitting, or duct_accessory.")
        };
    }

    private static MepStyleKind ResolveMepStyleKind(string kind)
    {
        var normalized = NormalizeMepKind(kind);
        return normalized switch
        {
            "pipe" => new MepStyleKind("pipe", RengaIds.PipeStyle, project => project.PipeStyles, null),
            "duct" => new MepStyleKind("duct", RengaIds.DuctStyle, project => project.DuctStyles, null),
            "electrical_circuit_line" or "electrical" or "electrical_line" => new MepStyleKind(
                "electrical_circuit_line", RengaIds.ElectricalCircuitLineStyle,
                project => project.ElectricalCircuitLineStyles, null),
            "pipe_fitting" => new MepStyleKind(
                "pipe_fitting", RengaIds.PipeFittingStyle,
                project => project.PipeFittingStyles, project => project.PipeFittingCategories),
            "pipe_accessory" => new MepStyleKind(
                "pipe_accessory", RengaIds.PipeAccessoryStyle,
                project => project.PipeAccessoryStyles, project => project.PipeAccessoryCategories),
            "duct_fitting" => new MepStyleKind(
                "duct_fitting", RengaIds.DuctFittingStyle,
                project => project.DuctFittingStyles, project => project.DuctFittingCategories),
            "duct_accessory" => new MepStyleKind(
                "duct_accessory", RengaIds.DuctAccessoryStyle,
                project => project.DuctAccessoryStyles, project => project.DuctAccessoryCategories),
            _ => throw new RengaException(
                "MEP style kind must be pipe, duct, electrical_circuit_line, pipe_fitting, " +
                "pipe_accessory, duct_fitting, or duct_accessory.")
        };
    }

    private static MepStyle ResolveMepStyle(
        IProject project,
        IEntityCollection collection,
        int? styleId,
        string kind)
    {
        if (styleId is int requested)
        {
            var entity = collection.GetById(requested)
                ?? throw new RengaException($"{kind} style {requested} was not found.");
            return new MepStyle(entity.Id, entity.Name);
        }

        if (collection.Count == 0)
        {
            throw new RengaException(
                $"No {kind} styles exist in the project. Create one with renga_create_mep_style.");
        }

        var first = collection.GetByIndex(0);
        return new MepStyle(first.Id, first.Name);
    }

    private static SystemCategory ResolveMepSystemCategory(string? value, string kind)
    {
        var fallback = kind switch
        {
            "pipe" => SystemCategory.SystemCategory_DomesticColdWater,
            "duct" => SystemCategory.SystemCategory_Ventilation,
            "electrical" => SystemCategory.SystemCategory_LightingCircuit,
            _ => SystemCategory.SystemCategory_Unknown
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = NormalizeMepKind(value);
        foreach (var item in MepSystemCategories)
        {
            if (string.Equals(item.Name, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Category.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    item.Category.ToString().Replace("SystemCategory_", string.Empty, StringComparison.Ordinal),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return item.Category;
            }
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric) &&
            Enum.IsDefined(typeof(SystemCategory), numeric))
        {
            return (SystemCategory)numeric;
        }

        throw new RengaException(
            $"Unknown system category '{value}' for {kind}. Use renga_list_mep_categories.");
    }

    private static string NormalizeMepKind(string kind) =>
        kind.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

    private static int ParseDuctProfile(string profile) => profile.Trim().ToLowerInvariant() switch
    {
        "circle" or "round" or "окружность" => 0,
        "rectangle" or "rectangular" or "прямоугольник" => 1,
        _ => throw new RengaException("Duct profile must be circle or rectangle.")
    };

    private sealed record MepLineKind(
        string Kind,
        string DisplayName,
        Guid TypeId,
        Func<IProject, IEntityCollection> Styles,
        SystemCategory DefaultCategory);

    private sealed record MepEquipmentKind(
        string Kind,
        Guid TypeId,
        Func<IProject, IEntityCollection> Styles,
        bool RequiresHost);

    private sealed record MepComponentKind(
        string Kind,
        Guid TypeId,
        Func<IProject, IEntityCollection> Styles,
        string AngleParameter);

    private sealed record MepStyleKind(
        string Kind,
        Guid TypeId,
        Func<IProject, IEntityCollection> Styles,
        Func<IProject, IEntityCollection>? Categories);

    private sealed record MepStyle(int Id, string Name);

    private sealed class RouteInfo
    {
        public int SourceId { get; set; }
        public int SourcePort { get; set; }
        public int TargetId { get; set; }
        public int TargetPort { get; set; }
        public int StyleId { get; set; }
        public string? StyleName { get; set; }
        public int Segments { get; set; }
        public int Joints { get; set; }
        public double Length { get; set; }
        public object? Start { get; set; }
        public object? End { get; set; }
        public int[] ObjectOnRouteIds { get; set; } = Array.Empty<int>();
    }

    private static readonly (string Name, SystemCategory Category, string Kind)[] MepSystemCategories =
    {
        ("domestic_cold_water", SystemCategory.SystemCategory_DomesticColdWater, "pipe"),
        ("domestic_hot_water", SystemCategory.SystemCategory_DomesticHotWater, "pipe"),
        ("domestic_sewerage", SystemCategory.SystemCategory_DomesticSewerage, "pipe"),
        ("domestic_gas_supply", SystemCategory.SystemCategory_DomesticGasSupply, "pipe"),
        ("water_fire_extinguishing", SystemCategory.SystemCategory_WaterFireExtinguishing, "pipe"),
        ("water_heating", SystemCategory.SystemCategory_WaterHeating, "pipe"),
        ("gas_fire_extinguishing", SystemCategory.SystemCategory_GasFireExtinguishing, "pipe"),
        ("storm_drain", SystemCategory.SystemCategory_StormDrain, "pipe"),
        ("industrial_cold_water", SystemCategory.SystemCategory_IndustrialColdWater, "pipe"),
        ("industrial_hot_water", SystemCategory.SystemCategory_IndustrialHotWater, "pipe"),
        ("industrial_sewerage", SystemCategory.SystemCategory_IndustrialSewerage, "pipe"),
        ("other_pipe_system", SystemCategory.SystemCategory_OtherPipeSystem, "pipe"),
        ("ventilation", SystemCategory.SystemCategory_Ventilation, "duct"),
        ("exhaust", SystemCategory.SystemCategory_Exhaust, "duct"),
        ("pressurization", SystemCategory.SystemCategory_Pressurization, "duct"),
        ("smoke_exhaust", SystemCategory.SystemCategory_SmokeExhaust, "duct"),
        ("vacuum", SystemCategory.SystemCategory_Vacuum, "duct"),
        ("other_duct_system", SystemCategory.SystemCategory_OtherDuctSystem, "duct"),
        ("lighting_circuit", SystemCategory.SystemCategory_LightingCircuit, "electrical"),
        ("power_circuit", SystemCategory.SystemCategory_PowerCircuit, "electrical"),
        ("other_electrical_system", SystemCategory.SystemCategory_OtherElectricalSystem, "electrical")
    };

    public object SaveProject(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RengaException("Project path must be non-empty.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".rnp", StringComparison.OrdinalIgnoreCase))
        {
            fullPath += ".rnp";
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var project = _connector.RequireProject();
        _connector.Check(project.SaveAs(fullPath, ProjectType.ProjectType_Project, true), "SaveAs");

        return new Dictionary<string, object?>
        {
            ["saved"] = true,
            ["path"] = fullPath,
            ["bytes"] = new FileInfo(fullPath).Length
        };
    }

    public object ExportIfc(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RengaException("IFC path must be non-empty.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".ifc", StringComparison.OrdinalIgnoreCase))
        {
            fullPath += ".ifc";
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var app = _connector.Connect();
        var project = _connector.RequireProject();
        var settings = app.CreateIfcExportSettings();
        var result = project.ExportToIfc2(fullPath, true, settings);
        if (result != 0)
        {
            result = project.ExportToIfc(fullPath, true);
        }

        _connector.Check(result, "ExportToIfc");

        return new Dictionary<string, object?>
        {
            ["exported"] = true,
            ["path"] = fullPath,
            ["bytes"] = new FileInfo(fullPath).Length
        };
    }

    public object ListLevels()
    {
        var project = _connector.RequireProject();
        var model = project.Model;
        return new Dictionary<string, object?>
        {
            ["project"] = DescribeProject(project, model),
            ["levels"] = ReadLevels(model).Select(ToDictionary).ToArray()
        };
    }

    public object ListObjects(string? type, int? limit)
    {
        var project = _connector.RequireProject();
        var model = project.Model;
        var max = Math.Clamp(limit ?? 500, 1, 5000);

        Guid? typeFilter = null;
        if (!string.IsNullOrWhiteSpace(type))
        {
            var trimmed = type.Trim();
            if (Guid.TryParse(trimmed, out var parsed))
            {
                typeFilter = parsed;
            }
            else if (FriendlyTypeNames.TryGetValue(trimmed, out var known))
            {
                typeFilter = known;
            }
            else
            {
                throw new RengaException(
                    $"Unknown object type '{type}'. Use a GUID or one of: {string.Join(", ", FriendlyTypeNames.Keys)}.");
            }
        }

        var items = new List<Dictionary<string, object?>>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (typeFilter is Guid filter && obj.ObjectType != filter)
            {
                continue;
            }

            items.Add(DescribeObject(obj));
        }

        return new Dictionary<string, object?>
        {
            ["total"] = items.Count,
            ["limit"] = max,
            ["truncated"] = items.Count > max,
            ["items"] = items.Take(max).ToArray()
        };
    }

    public object GetObject(int objectId, bool includeProperties)
    {
        var project = _connector.RequireProject();
        var obj = FindObject(project.Model, objectId)
            ?? throw new RengaException($"Object with ID {objectId} was not found.");

        var result = DescribeObject(obj);
        result["parameters"] = DescribeParameters(obj, project);
        if (includeProperties)
        {
            result["properties"] = DescribeProperties(obj);
        }

        return result;
    }

    public object ListParameters(int objectId)
    {
        var project = _connector.RequireProject();
        var obj = FindObject(project.Model, objectId)
            ?? throw new RengaException($"Object with ID {objectId} was not found.");

        var result = DescribeObject(obj);
        result["parameters"] = DescribeParameters(obj, project);
        return result;
    }

    public object SetParameter(int objectId, string parameterName, JsonElement value, string? unit)
    {
        var project = _connector.RequireProject();
        var obj = FindObject(project.Model, objectId)
            ?? throw new RengaException($"Object with ID {objectId} was not found.");

        var container = obj.GetParameters();
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException(
                $"Parameter '{parameterName}' was not found on object {objectId}. " +
                "Use renga_list_parameters to inspect available parameter names and GUIDs.");

        var clrValue = ParseJsonParameterValue(value, parameter);
        return ApplyParameterValue(project, obj.Id, obj.ObjectTypeS, parameter, clrValue, unit);
    }

    public object ListMaterials(string? kind, int? limit)
    {
        var project = _connector.RequireProject();
        var max = Math.Clamp(limit ?? 500, 1, 5000);
        var normalized = string.IsNullOrWhiteSpace(kind) ? "all" : kind.Trim().ToLowerInvariant();
        var includeMaterials = normalized is "all" or "material";
        var includeLayered = normalized is "all" or "layered" or "layered_material";
        if (!includeMaterials && !includeLayered)
        {
            throw new RengaException("Material kind must be one of: all, material, layered.");
        }

        var materials = new List<Dictionary<string, object?>>();
        if (includeMaterials)
        {
            var collection = project.Materials;
            for (var i = 0; i < collection.Count && materials.Count < max; i++)
            {
                var entity = collection.GetByIndex(i);
                var material = entity as IMaterial;
                var color = material?.Color;
                materials.Add(new Dictionary<string, object?>
                {
                    ["id"] = entity.Id,
                    ["uniqueId"] = entity.UniqueIdS,
                    ["name"] = material?.Name ?? entity.Name,
                    ["density"] = material?.Density,
                    ["color"] = color is null
                        ? null
                        : new Dictionary<string, object?>
                        {
                            ["r"] = color.Value.Red,
                            ["g"] = color.Value.Green,
                            ["b"] = color.Value.Blue,
                            ["a"] = color.Value.Alpha
                        }
                });
            }
        }

        var layeredMaterials = new List<Dictionary<string, object?>>();
        if (includeLayered)
        {
            var collection = project.LayeredMaterials;
            for (var i = 0; i < collection.Count && layeredMaterials.Count < max; i++)
            {
                var entity = collection.GetByIndex(i);
                var layered = entity as ILayeredMaterial;
                var layers = new List<Dictionary<string, object?>>();
                if (layered is not null)
                {
                    try
                    {
                        var layerCollection = layered.Layers;
                        for (var j = 0; j < layerCollection.Count; j++)
                        {
                            var layer = layerCollection.Get(j);
                            layers.Add(new Dictionary<string, object?>
                            {
                                ["id"] = layer.Id,
                                ["thickness"] = Units.ToMeters(layer.Thickness),
                                ["materialId"] = layer.Material?.Id,
                                ["materialName"] = layer.Material?.Name
                            });
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                layeredMaterials.Add(new Dictionary<string, object?>
                {
                    ["id"] = entity.Id,
                    ["uniqueId"] = entity.UniqueIdS,
                    ["name"] = layered?.Name ?? entity.Name,
                    ["baseLayerIndex"] = layered?.BaseLayerIndex,
                    ["layers"] = layers.ToArray()
                });
            }
        }

        return new Dictionary<string, object?>
        {
            ["materials"] = materials.ToArray(),
            ["layeredMaterials"] = layeredMaterials.ToArray(),
            ["materialCount"] = project.Materials.Count,
            ["layeredMaterialCount"] = project.LayeredMaterials.Count
        };
    }

    public object ListStyles(string? kind, int? limit)
    {
        var project = _connector.RequireProject();
        var max = Math.Clamp(limit ?? 500, 1, 5000);

        if (string.IsNullOrWhiteSpace(kind))
        {
            var collections = new List<Dictionary<string, object?>>();
            foreach (var pair in StyleCollections)
            {
                try
                {
                    collections.Add(new Dictionary<string, object?>
                    {
                        ["kind"] = pair.Key,
                        ["count"] = pair.Value(project).Count
                    });
                }
                catch
                {
                    // ignored
                }
            }

            return new Dictionary<string, object?> { ["collections"] = collections.ToArray() };
        }

        var normalized = kind.Trim().ToLowerInvariant();
        if (!StyleCollections.TryGetValue(normalized, out var getter))
        {
            throw new RengaException(
                $"Unknown style kind '{kind}'. Allowed values: {string.Join(", ", StyleCollections.Keys)}.");
        }

        var collection = getter(project);
        var total = collection.Count;
        var items = new List<Dictionary<string, object?>>();
        for (var i = 0; i < total && items.Count < max; i++)
        {
            var entity = collection.GetByIndex(i);
            items.Add(new Dictionary<string, object?>
            {
                ["id"] = entity.Id,
                ["uniqueId"] = entity.UniqueIdS,
                ["name"] = entity.Name,
                ["typeId"] = entity.TypeIdS
            });
        }

        return new Dictionary<string, object?>
        {
            ["kind"] = normalized,
            ["total"] = total,
            ["limit"] = max,
            ["truncated"] = total > max,
            ["items"] = items.ToArray()
        };
    }

    public object AssignMaterial(int objectId, int materialId, bool? layered)
    {
        var project = _connector.RequireProject();
        var obj = FindObject(project.Model, objectId)
            ?? throw new RengaException($"Object with ID {objectId} was not found.");

        var material = FindEntityById(project.Materials, materialId);
        var layeredMaterial = FindEntityById(project.LayeredMaterials, materialId);

        string materialKind;
        string materialName;
        if (layered == true)
        {
            if (layeredMaterial is null)
            {
                throw new RengaException($"Layered material with ID {materialId} was not found.");
            }

            materialKind = "layered";
            materialName = layeredMaterial.Name;
        }
        else if (layered == false)
        {
            if (material is null)
            {
                throw new RengaException($"Material with ID {materialId} was not found.");
            }

            materialKind = "material";
            materialName = material.Name;
        }
        else if (material is not null)
        {
            materialKind = "material";
            materialName = material.Name;
        }
        else if (layeredMaterial is not null)
        {
            materialKind = "layered";
            materialName = layeredMaterial.Name;
        }
        else
        {
            throw new RengaException($"Material or layered material with ID {materialId} was not found.");
        }

        var parameterName = materialKind == "layered" ? "LayeredMaterialId" : "MaterialId";
        var container = obj.GetParameters();
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException(
                materialKind == "layered"
                    ? "This object does not expose LayeredMaterialId. Walls, floors, and roofs typically use layered materials."
                    : "This object does not expose MaterialId. Use a layered material for walls, floors, and roofs.");

        var result = (Dictionary<string, object?>)ApplyParameterValue(
            project,
            obj.Id,
            obj.ObjectTypeS,
            parameter,
            materialId,
            null);
        result["materialId"] = materialId;
        result["materialName"] = materialName;
        result["materialKind"] = materialKind;
        return result;
    }

    public object CreateMaterial(string name, double? density)
    {
        name = RequireName(name, "name");
        var project = _connector.RequireProject();
        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = project.Materials.CreateNewEntityArgs();
            args.TypeId = RengaIds.Material;
            var entity = project.Materials.Create(args)
                ?? throw new RengaException("Renga did not create the material. " + _connector.LastError);

            var container = entity as IParameterContainer
                ?? entity.GetInterfaceByName("IParameterContainer") as IParameterContainer
                ?? throw new RengaException("The created material does not expose IParameterContainer.");

            SetContainerString(container, RengaParams.EntityName, name);
            if (density is double value)
            {
                SetContainerDouble(container, RengaParams.MassDensity, value);
            }

            op.Apply();
            applied = true;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["id"] = entity.Id,
                ["uniqueId"] = entity.UniqueIdS,
                ["name"] = name,
                ["density"] = density
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object CreateStyle(string kind, string name, int? materialId, int? profileId)
    {
        name = RequireName(name, "name");
        var normalized = kind.Trim().ToLowerInvariant();
        if (!StyleTypeIds.TryGetValue(normalized, out var typeId))
        {
            throw new RengaException("Style creation supports: door, window, beam, column, plate.");
        }

        var project = _connector.RequireProject();
        var collection = StyleCollections[normalized](project);
        var op = StartOperation(project);
        var applied = false;
        try
        {
            var args = collection.CreateNewEntityArgs();
            args.TypeId = typeId;
            var entity = collection.Create(args)
                ?? throw new RengaException("Renga did not create the style. " + _connector.LastError);

            var container = GetEntityContainer(entity);
            SetContainerString(container, RengaParams.EntityName, name);
            if (materialId is int material)
            {
                SetContainerInt(container, RengaParams.MaterialId, material);
            }

            if (profileId is int profile)
            {
                SetContainerInt(container, RengaParams.ProfileId, profile);
            }

            op.Apply();
            applied = true;

            var warning = normalized is "beam" or "column" or "plate" &&
                          profileId is null &&
                          project.Profiles.Count == 0
                ? "No profiles exist in this project; the style may have no usable profile geometry."
                : null;

            return new Dictionary<string, object?>
            {
                ["created"] = true,
                ["kind"] = normalized,
                ["id"] = entity.Id,
                ["uniqueId"] = entity.UniqueIdS,
                ["name"] = name,
                ["typeId"] = entity.TypeIdS,
                ["materialId"] = materialId,
                ["profileId"] = profileId,
                ["warning"] = warning,
                ["parameters"] = DescribeContainerParameters(container, project)
            };
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    public object ListStyleParameters(string kind, int styleId)
    {
        var project = _connector.RequireProject();
        var entity = FindStyleEntity(project, kind, styleId);
        var container = GetEntityContainer(entity);

        return new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["id"] = entity.Id,
            ["uniqueId"] = entity.UniqueIdS,
            ["name"] = entity.Name,
            ["typeId"] = entity.TypeIdS,
            ["parameters"] = DescribeContainerParameters(container, project)
        };
    }

    public object SetStyleParameter(
        string kind,
        int styleId,
        string parameterName,
        JsonElement value,
        string? unit)
    {
        var project = _connector.RequireProject();
        var entity = FindStyleEntity(project, kind, styleId);
        var container = GetEntityContainer(entity);
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException(
                $"Parameter '{parameterName}' was not found on style {styleId}. " +
                "Use renga_list_style_parameters to inspect it.");

        var clrValue = ParseJsonParameterValue(value, parameter);
        return ApplyParameterValue(project, entity.Id, entity.TypeIdS, parameter, clrValue, unit);
    }

    public object ListPropertyDefinitions()
    {
        var project = _connector.RequireProject();
        var manager = project.PropertyManager;
        var definitions = new List<Dictionary<string, object?>>();
        for (var i = 0; i < manager.PropertyCount; i++)
        {
            try
            {
                var id = manager.GetPropertyId(i);
                var name = manager.GetPropertyName(id);
                var type = manager.GetPropertyType(id);
                string[] enumeration;
                try
                {
                    enumeration = manager.GetPropertyDescription2(id)?.GetEnumerationItems() ?? Array.Empty<string>();
                }
                catch
                {
                    enumeration = Array.Empty<string>();
                }

                definitions.Add(new Dictionary<string, object?>
                {
                    ["id"] = id.ToString("B"),
                    ["name"] = name,
                    ["type"] = PropertyTypeName(type),
                    ["enumeration"] = enumeration
                });
            }
            catch
            {
                // ignored
            }
        }

        return new Dictionary<string, object?>
        {
            ["count"] = manager.PropertyCount,
            ["definitions"] = definitions.ToArray()
        };
    }

    public object RegisterProperty(
        string name,
        string type,
        IReadOnlyList<string>? enumeration,
        string? propertyId)
    {
        name = RequireName(name, "name");
        var propertyType = ParsePropertyType(type);
        var guid = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(propertyId) && !Guid.TryParse(propertyId, out guid))
        {
            throw new RengaException("propertyId must be a GUID.");
        }

        var project = _connector.RequireProject();
        var manager = project.PropertyManager;
        if (manager.IsPropertyRegistered(guid))
        {
            throw new RengaException($"Property {guid:B} is already registered.");
        }

        var description = manager.CreatePropertyDescription(name, propertyType);
        if (enumeration is { Count: > 0 })
        {
            description.SetEnumerationItems(enumeration.ToArray());
        }

        manager.RegisterProperty2(guid, description);

        return new Dictionary<string, object?>
        {
            ["created"] = true,
            ["id"] = guid.ToString("B"),
            ["name"] = name,
            ["type"] = PropertyTypeName(propertyType),
            ["enumeration"] = enumeration ?? Array.Empty<string>()
        };
    }

    public object AssignProperty(string propertyId, string objectType)
    {
        var project = _connector.RequireProject();
        if (!Guid.TryParse(propertyId, out var guid))
        {
            throw new RengaException("propertyId must be a GUID.");
        }

        var manager = project.PropertyManager;
        if (!manager.IsPropertyRegistered(guid))
        {
            throw new RengaException($"Property {propertyId} is not registered.");
        }

        var typeGuid = ResolveObjectTypeGuid(objectType);
        manager.AssignPropertyToType(guid, typeGuid);

        return new Dictionary<string, object?>
        {
            ["assigned"] = true,
            ["propertyId"] = guid.ToString("B"),
            ["objectType"] = objectType,
            ["objectTypeId"] = typeGuid.ToString("B")
        };
    }

    public object UnassignProperty(string propertyId, string objectType)
    {
        var project = _connector.RequireProject();
        if (!Guid.TryParse(propertyId, out var guid))
        {
            throw new RengaException("propertyId must be a GUID.");
        }

        var typeGuid = ResolveObjectTypeGuid(objectType);
        project.PropertyManager.UnassignPropertyFromType(guid, typeGuid);

        return new Dictionary<string, object?>
        {
            ["unassigned"] = true,
            ["propertyId"] = guid.ToString("B"),
            ["objectType"] = objectType,
            ["objectTypeId"] = typeGuid.ToString("B")
        };
    }

    public object UnregisterProperty(string propertyId)
    {
        var project = _connector.RequireProject();
        if (!Guid.TryParse(propertyId, out var guid))
        {
            throw new RengaException("propertyId must be a GUID.");
        }

        if (!project.PropertyManager.IsPropertyRegistered(guid))
        {
            throw new RengaException($"Property {propertyId} is not registered.");
        }

        project.PropertyManager.UnregisterProperty(guid);

        return new Dictionary<string, object?>
        {
            ["removed"] = true,
            ["propertyId"] = guid.ToString("B")
        };
    }

    public object SetProperty(int objectId, string propertyId, JsonElement value)
    {
        var project = _connector.RequireProject();
        var obj = FindObject(project.Model, objectId)
            ?? throw new RengaException($"Object with ID {objectId} was not found.");

        if (!Guid.TryParse(propertyId, out var guid))
        {
            throw new RengaException("propertyId must be a GUID.");
        }

        IPropertyContainer container;
        try
        {
            container = obj.GetProperties();
        }
        catch
        {
            throw new RengaException($"Object {objectId} does not support custom properties.");
        }

        if (!container.Contains(guid))
        {
            throw new RengaException(
                $"Property {propertyId} is not assigned to this object's type. " +
                "Use renga_assign_property first.");
        }

        var property = container.Get(guid);
        var oldHasValue = property.HasValue();
        var (oldValue, oldUnit) = oldHasValue ? GetPropertyValue(property) : (null, null);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            switch (property.Type)
            {
                case PropertyType.PropertyType_Double:
                    property.SetDoubleValue(ToDouble(ParseDouble(value)));
                    break;
                case PropertyType.PropertyType_String:
                    property.SetStringValue((string)ParseString(value));
                    break;
                case PropertyType.PropertyType_Angle:
                    property.SetAngleValue(ToDouble(ParseDouble(value)), AngleUnit.AngleUnit_Degrees);
                    break;
                case PropertyType.PropertyType_Area:
                    property.SetAreaValue(ToDouble(ParseDouble(value)), AreaUnit.AreaUnit_Meters2);
                    break;
                case PropertyType.PropertyType_Boolean:
                    property.SetBooleanValue((bool)ParseBool(value));
                    break;
                case PropertyType.PropertyType_Enumeration:
                    var enumerationValue = (string)ParseString(value);
                    ValidatePropertyEnumeration(project, guid, enumerationValue);
                    property.SetEnumerationValue(enumerationValue);
                    break;
                case PropertyType.PropertyType_Integer:
                    property.SetIntegerValue(ToInteger(value));
                    break;
                case PropertyType.PropertyType_Length:
                    property.SetLengthValue(ToDouble(ParseDouble(value)), LengthUnit.LengthUnit_Meters);
                    break;
                case PropertyType.PropertyType_Logical:
                    property.SetLogicalValue((bool)ParseBool(value) ? Logical.Logical_True : Logical.Logical_False);
                    break;
                case PropertyType.PropertyType_Mass:
                    property.SetMassValue(ToDouble(ParseDouble(value)), MassUnit.MassUnit_Kilograms);
                    break;
                case PropertyType.PropertyType_Volume:
                    property.SetVolumeValue(ToDouble(ParseDouble(value)), VolumeUnit.VolumeUnit_Meters3);
                    break;
                default:
                    throw new RengaException($"Property type {PropertyTypeName(property.Type)} is not supported for writing.");
            }

            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        var newHasValue = property.HasValue();
        var (newValue, newUnit) = newHasValue ? GetPropertyValue(property) : (null, null);

        return new Dictionary<string, object?>
        {
            ["updated"] = true,
            ["objectId"] = objectId,
            ["propertyId"] = guid.ToString("B"),
            ["propertyName"] = property.Name,
            ["propertyType"] = PropertyTypeName(property.Type),
            ["oldValue"] = oldValue,
            ["oldUnit"] = oldUnit,
            ["newValue"] = newValue,
            ["newUnit"] = newUnit
        };
    }

    private object RebuildSeed(IProject project, IModel model)
    {
        if (HasGeometry(model))
        {
            throw new RengaException("The project contains geometry; the level/axis skeleton cannot be rebuilt.");
        }

        if (_state.AxesLocked)
        {
            throw new RengaException("The project contains axes whose coordinates are unknown to this session.");
        }

        if (_state.Levels.Count == 0)
        {
            _state.Levels.Add(new SeedLevel("Level 1", 0));
        }

        var app = _connector.Connect();
        var directory = Path.Combine(Path.GetTempPath(), "RengaMcp");
        Directory.CreateDirectory(directory);
        var ifcPath = Path.Combine(directory, $"seed-{Guid.NewGuid():N}.ifc");
        File.WriteAllText(ifcPath, IfcSeedBuilder.Build(_state.Levels, _state.Axes), new UTF8Encoding(false));

        try
        {
            if (app.HasProject())
            {
                _connector.Check(app.CloseProject(true), "CloseProject");
            }

            _connector.Check(app.ImportIfcProject(ifcPath), "ImportIfcProject");
            if (!app.HasProject())
            {
                throw new RengaException("ImportIfcProject returned without an open project.");
            }

            var importedProject = app.Project;
            var importedModel = importedProject.Model;
            VerifySeed(importedModel);
            RemoveImportCarriers(importedProject, importedModel);
            VerifySeed(importedModel);

            _state.Levels = ReadLevels(importedModel)
                .Select(level => new SeedLevel(level.Name, level.ElevationMm))
                .ToList();
            _state.Managed = true;
            _state.FreshDefault = false;
            _state.AxesLocked = false;
            _stateProjectId = GetProjectStateId(importedProject, importedModel);
            app.Visible = _connector.Visible;

            return new Dictionary<string, object?>
            {
                ["project"] = DescribeProject(importedProject, importedModel),
                ["levels"] = ReadLevels(importedModel).Select(ToDictionary).ToArray(),
                ["axes"] = ReadAxes(importedModel).Select(ToDictionary).ToArray()
            };
        }
        finally
        {
            try
            {
                File.Delete(ifcPath);
            }
            catch
            {
                // ignored
            }
        }
    }

    private void VerifySeed(IModel model)
    {
        var levels = ReadLevels(model);
        if (levels.Count != _state.Levels.Count)
        {
            throw new RengaException(
                $"IFC seed verification failed: expected {_state.Levels.Count} levels, found {levels.Count}.");
        }

        foreach (var expected in _state.Levels)
        {
            var actual = levels.FirstOrDefault(level =>
                string.Equals(level.Name, expected.Name, StringComparison.Ordinal))
                ?? throw new RengaException($"IFC seed verification failed: level '{expected.Name}' is missing.");
            if (Math.Abs(actual.ElevationMm - expected.ElevationMm) > 1e-6)
            {
                throw new RengaException(
                    $"IFC seed verification failed: level '{expected.Name}' elevation is " +
                    $"{actual.ElevationMm.ToString(CultureInfo.InvariantCulture)} mm instead of " +
                    $"{expected.ElevationMm.ToString(CultureInfo.InvariantCulture)} mm.");
            }
        }

        var axes = ReadAxes(model);
        if (axes.Count != _state.Axes.Count)
        {
            throw new RengaException(
                $"IFC seed verification failed: expected {_state.Axes.Count} axes, found {axes.Count}.");
        }

        foreach (var expected in _state.Axes)
        {
            if (!axes.Any(axis => string.Equals(axis.Name, expected.Name, StringComparison.Ordinal)))
            {
                throw new RengaException($"IFC seed verification failed: axis '{expected.Name}' is missing.");
            }
        }
    }

    private static void RemoveImportCarriers(IProject project, IModel model)
    {
        var objects = model.GetObjects();
        var ids = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (!IsLevel(obj) && !IsAxis(obj))
            {
                ids.Add(obj.Id);
            }
        }

        if (ids.Count == 0)
        {
            return;
        }

        var op = StartOperation(project);
        var applied = false;
        try
        {
            foreach (var id in ids)
            {
                model.DeleteObjectById(id);
            }

            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }
    }

    private void EnsureState(IProject project, IModel model)
    {
        var projectId = GetProjectStateId(project, model);
        if (_stateProjectId == projectId)
        {
            return;
        }

        _stateProjectId = projectId;
        _state = new SeedState();

        var levels = ReadLevels(model);
        _state.Levels = levels.Select(level => new SeedLevel(level.Name, level.ElevationMm)).ToList();

        var axes = ReadAxes(model);
        _state.AxesLocked = axes.Count > 0;
        _state.Managed = false;
        _state.FreshDefault = false;
    }

    private static Guid GetProjectStateId(IProject project, IModel model)
    {
        try
        {
            var id = project.ProjectInfo.UniqueId;
            if (id != Guid.Empty)
            {
                return id;
            }
        }
        catch
        {
            // ignored
        }

        return model.Id;
    }

    private static IOperation StartOperation(IProject project)
    {
        if (project.HasActiveOperation())
        {
            throw new RengaException("Renga already has an active operation. Finish the previous tool call first.");
        }

        return project.StartOperation();
    }

    private static void TryRollback(IOperation operation)
    {
        try
        {
            operation.Rollback();
        }
        catch
        {
            // ignored
        }
    }

    private ICurve2D CreateLine2D(double x1, double y1, double x2, double y2) =>
        _connector.Connect().Math.CreateLineSegment2D(
            new Point2D { X = x1, Y = y1 },
            new Point2D { X = x2, Y = y2 });

    private ICurve2D CreateClosedPolygon(IReadOnlyList<(double X, double Y)> points)
    {
        var segments = new List<ICurve2D>();
        for (var i = 0; i < points.Count; i++)
        {
            var start = points[i];
            var end = points[(i + 1) % points.Count];
            segments.Add(CreateLine2D(start.X, start.Y, end.X, end.Y));
        }

        return _connector.Connect().Math.CreateCompositeCurve2D(segments.ToArray());
    }

    private static Placement3D IdentityPlacement() => new()
    {
        Origin = new Point3D { X = 0, Y = 0, Z = 0 },
        xAxis = new Vector3D { X = 1, Y = 0, Z = 0 },
        zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
    };

    private static void SetDoubleParameter(IModelObject obj, Guid parameterId, double value)
    {
        var parameters = obj.GetParameters();
        if (!parameters.Contains(parameterId))
        {
            throw new RengaException($"Required parameter {parameterId:B} is not available on {obj.ObjectTypeS}.");
        }

        var parameter = parameters.Get(parameterId);
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter {parameterId:B} is read-only.");
        }

        parameter.SetDoubleValue(value);
    }

    private static int FirstStyleId(IEntityCollection collection)
    {
        try
        {
            return collection.Count > 0 ? collection.GetByIndex(0).Id : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double PolygonArea(IReadOnlyList<(double X, double Y)> points)
    {
        double twiceArea = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            twiceArea += a.X * b.Y - b.X * a.Y;
        }

        return Math.Abs(twiceArea) / 2.0;
    }

    private static object DescribeBaseline2D(IModelObject obj)
    {
        try
        {
            var baseline = (obj as IBaseline2DObject)?.GetBaseline();
            if (baseline is null)
            {
                return new { available = false };
            }

            var begin = baseline.GetBeginPoint();
            var end = baseline.GetEndPoint();
            return new
            {
                available = true,
                begin = new { x = Units.ToMeters(begin.X), y = Units.ToMeters(begin.Y) },
                end = new { x = Units.ToMeters(end.X), y = Units.ToMeters(end.Y) },
                length = Units.ToMeters(baseline.GetLength())
            };
        }
        catch
        {
            return new { available = false };
        }
    }

    private sealed class LevelInfo
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string UniqueId { get; init; } = string.Empty;
        public double ElevationMm { get; init; }
        public double OriginZMm { get; init; }
    }

    private sealed class AxisInfo
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string UniqueId { get; init; } = string.Empty;
    }

    private static List<LevelInfo> ReadLevels(IModel model)
    {
        var result = new List<LevelInfo>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (obj is not ILevel level)
            {
                continue;
            }

            result.Add(new LevelInfo
            {
                Id = obj.Id,
                Name = level.LevelName,
                UniqueId = obj.UniqueIdS,
                ElevationMm = level.Elevation,
                OriginZMm = level.Placement.Origin.Z
            });
        }

        return result.OrderBy(level => level.ElevationMm).ThenBy(level => level.Name).ToList();
    }

    private static List<AxisInfo> ReadAxes(IModel model)
    {
        var result = new List<AxisInfo>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (!IsAxis(obj))
            {
                continue;
            }

            var name = string.Empty;
            try
            {
                var parameters = obj.GetParameters();
                if (parameters.Contains(RengaParams.GridLineName))
                {
                    name = parameters.Get(RengaParams.GridLineName).GetStringValue();
                }
            }
            catch
            {
                // ignored
            }

            result.Add(new AxisInfo
            {
                Id = obj.Id,
                Name = name,
                UniqueId = obj.UniqueIdS
            });
        }

        return result;
    }

    private static bool HasGeometry(IModel model)
    {
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (!IsLevel(obj) && !IsAxis(obj))
            {
                return true;
            }
        }

        return false;
    }

    private static INewEntityArgs CreateBaselineArgs(
        IModel model,
        Guid typeId,
        int levelId,
        double x1,
        double y1,
        double x2,
        double y2,
        out double lengthMm)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-6)
        {
            throw new RengaException("Start and end points must be different.");
        }

        lengthMm = length;
        var args = model.CreateNewEntityArgs();
        args.TypeId = typeId;
        args.HostObjectId = levelId;
        args.Placement3D = new Placement3D
        {
            Origin = new Point3D { X = x1, Y = y1, Z = 0 },
            xAxis = new Vector3D { X = dx / length, Y = dy / length, Z = 0 },
            zAxis = new Vector3D { X = 0, Y = 0, Z = 1 }
        };

        return args;
    }

    private static List<(double X, double Y)> ResolvePlanContour(
        IReadOnlyList<(double X, double Y)>? points,
        double? x1,
        double? y1,
        double? x2,
        double? y2,
        string label)
    {
        IReadOnlyList<(double X, double Y)> contour;
        if (points is { Count: >= 3 })
        {
            contour = points;
        }
        else if (x1 is double rx1 && y1 is double ry1 && x2 is double rx2 && y2 is double ry2)
        {
            contour = new[]
            {
                (rx1, ry1),
                (rx2, ry1),
                (rx2, ry2),
                (rx1, ry2)
            };
        }
        else
        {
            throw new RengaException(
                $"Provide either a points array with at least 3 points or x1, y1, x2, y2 for {label}.");
        }

        var polygon = contour
            .Select(point => (
                X: Units.ToMillimeters(RequireFinite(point.X, "point.x")),
                Y: Units.ToMillimeters(RequireFinite(point.Y, "point.y"))))
            .ToList();

        if (polygon.Count > 1 &&
            Math.Abs(polygon[0].X - polygon[^1].X) < 1e-6 &&
            Math.Abs(polygon[0].Y - polygon[^1].Y) < 1e-6)
        {
            polygon.RemoveAt(polygon.Count - 1);
        }

        if (polygon.Count < 3 ||
            polygon.Select(point => (point.X, point.Y)).Distinct().Count() < 3)
        {
            throw new RengaException($"The contour for {label} requires at least 3 distinct points.");
        }

        return polygon;
    }

    private static (double X, double Y) BoundingBoxCenter(IReadOnlyList<(double X, double Y)> polygon)
    {
        var minX = polygon.Min(point => point.X);
        var maxX = polygon.Max(point => point.X);
        var minY = polygon.Min(point => point.Y);
        var maxY = polygon.Max(point => point.Y);
        return ((minX + maxX) / 2.0, (minY + maxY) / 2.0);
    }

    private static double Perimeter(IReadOnlyList<(double X, double Y)> polygon)
    {
        double length = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var start = polygon[i];
            var end = polygon[(i + 1) % polygon.Count];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            length += Math.Sqrt(dx * dx + dy * dy);
        }

        return length;
    }

    private static void SetDoubleParameterByName(IModelObject obj, string parameterName, double value)
    {
        var container = obj.GetParameters();
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException(
                $"Parameter '{parameterName}' is not available on {obj.ObjectTypeS}.");
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter '{parameterName}' is read-only.");
        }

        parameter.SetDoubleValue(value);
    }

    private static void SetStringParameterByName(IModelObject obj, string parameterName, string value)
    {
        var container = obj.GetParameters();
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException(
                $"Parameter '{parameterName}' is not available on {obj.ObjectTypeS}.");
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter '{parameterName}' is read-only.");
        }

        parameter.SetStringValue(value);
    }

    private static void ApplyLateParameters(
        IProject project,
        IModelObject obj,
        IReadOnlyList<(string Name, int Value)> intParameters,
        IReadOnlyList<(string Name, double Value)> doubleParameters,
        int? materialId)
    {
        if (intParameters.Count == 0 && doubleParameters.Count == 0 && materialId is null)
        {
            return;
        }

        ApplyLateContainerParameters(
            project,
            obj.GetParameters(),
            $"Object {obj.Id}",
            intParameters,
            doubleParameters,
            materialId);
    }

    private static void ApplyLateContainerParameters(
        IProject project,
        IParameterContainer container,
        string label,
        IReadOnlyList<(string Name, int Value)> intParameters,
        IReadOnlyList<(string Name, double Value)> doubleParameters,
        int? materialId)
    {
        if (intParameters.Count == 0 && doubleParameters.Count == 0 && materialId is null)
        {
            return;
        }

        var op = StartOperation(project);
        var applied = false;
        try
        {
            foreach (var (name, value) in intParameters)
            {
                var parameter = FindParameter(container, name)
                    ?? throw new RengaException($"Parameter '{name}' is not available on {label}.");
                parameter.SetIntValue(value);
            }

            foreach (var (name, value) in doubleParameters)
            {
                var parameter = FindParameter(container, name)
                    ?? throw new RengaException($"Parameter '{name}' is not available on {label}.");
                parameter.SetDoubleValue(value);
            }

            if (materialId is int material)
            {
                var parameter = FindParameter(container, "MaterialId")
                    ?? throw new RengaException($"{label} does not expose MaterialId.");
                parameter.SetIntValue(material);
            }

            op.Apply();
            applied = true;
        }
        catch (Exception ex)
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw new RengaException(
                $"{label} was created, but additional parameters could not be applied: {ex.Message}",
                ex);
        }
    }

    private static (int ContourCount, double OuterLength) DescribeRoomRegion(
        IModelObject obj,
        double xMm,
        double yMm)
    {
        try
        {
            var room = obj.GetInterfaceByName("IRoom") as IRoom;
            var region = room?.CalculateRegion(new Point2D { X = xMm, Y = yMm })?.Region;
            if (region is null)
            {
                return (0, 0);
            }

            var outer = region.GetOuterContour();
            return (region.GetContourCount(), Units.ToMeters(outer?.GetLength() ?? 0));
        }
        catch
        {
            return (0, 0);
        }
    }

    private static int ParseWallFoundationShape(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "rectangular" or "rectangle" or "прямоугольный" => 0,
        "trapezoid" or "trapezoidal" or "трапециевидный" => 1,
        _ => throw new RengaException("Foundation shape must be rectangular or trapezoid.")
    };

    private static int ParseStairShape(string shape) => shape.Trim().ToLowerInvariant() switch
    {
        "contour" or "контурная" => 0,
        "thickened" or "утолщённая" or "утолщенная" => 1,
        "solid" or "сплошная" => 2,
        "open" or "открытая" => 3,
        _ => throw new RengaException("Stair shape must be contour, thickened, solid, or open.")
    };

    private static int ParsePosition(string position) => position.Trim().ToLowerInvariant() switch
    {
        "right" or "справа" => 0,
        "center" or "centre" or "по центру" => 1,
        "left" or "слева" => 2,
        _ => throw new RengaException("Position must be right, center, or left.")
    };

    private static Dictionary<string, object?>[] DescribeDrawings(IProject project)
    {
        var result = new List<Dictionary<string, object?>>();
        for (var i = 0; i < project.Drawings2.Count; i++)
        {
            result.Add(DescribeDrawing(project, project.Drawings2.GetByIndex(i)));
        }

        return result.ToArray();
    }

    private static Dictionary<string, object?> DescribeDrawing(IProject project, IEntity entity)
    {
        var container = GetEntityContainer(entity);
        var pageFormatId = GetIntParameter(container, "PageFormatId") ?? 0;
        var layoutStyleId = GetIntParameter(container, "LayoutStyleId") ?? 0;
        var drawingId = entity.UniqueIdS;
        var legacy = FindLegacyDrawing(project, drawingId);

        int titleBlocks = 0;
        try
        {
            titleBlocks = legacy?.TitleBlockInstanceCount ?? 0;
        }
        catch
        {
            // ignored
        }

        return new Dictionary<string, object?>
        {
            ["index"] = FindDrawingIndex(project, entity.Id),
            ["entityId"] = entity.Id,
            ["uniqueId"] = drawingId,
            ["drawingId"] = drawingId,
            ["name"] = GetStringParameter(container, "EntityName") ?? entity.Name,
            ["pageFormatId"] = pageFormatId,
            ["pageFormatName"] = pageFormatId > 0
                ? FindEntityName(project.PageFormatStyles, pageFormatId)
                : null,
            ["layoutStyleId"] = layoutStyleId,
            ["layoutStyleName"] = layoutStyleId > 0
                ? FindEntityName(project.LayoutStyles, layoutStyleId)
                : null,
            ["titleBlocks"] = titleBlocks
        };
    }

    private static IEntity RequireDrawingEntity(IProject project, string drawingId)
    {
        if (!Guid.TryParse(drawingId, out _))
        {
            throw new RengaException(
                "drawingId must be a GUID from renga_list_drawings.");
        }

        IEntity? entity;
        try
        {
            entity = project.Drawings2.GetByUniqueIdS(drawingId);
        }
        catch
        {
            entity = null;
        }

        if (entity is null)
        {
            throw new RengaException(
                $"Drawing {drawingId} was not found. Use renga_list_drawings to inspect drawings.");
        }

        if (entity.TypeId != RengaIds.Drawing)
        {
            throw new RengaException($"Entity {drawingId} is not a drawing.");
        }

        return entity;
    }

    private static IDrawing? FindLegacyDrawing(IProject project, string drawingId)
    {
        if (!Guid.TryParse(drawingId, out var guid))
        {
            return null;
        }

        try
        {
            for (var i = 0; i < project.Drawings.Count; i++)
            {
                try
                {
                    var drawing = project.Drawings.Get(i);
                    if (drawing.Id == guid)
                    {
                        return drawing;
                    }
                }
                catch
                {
                    // a stale entry should not prevent matching the other drawings
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static int FindDrawingIndex(IProject project, int entityId)
    {
        for (var i = 0; i < project.Drawings2.Count; i++)
        {
            if (project.Drawings2.GetByIndex(i).Id == entityId)
            {
                return i;
            }
        }

        return -1;
    }

    private static IEntity ResolveEntityReference(
        IEntityCollection collection,
        string reference,
        string label)
    {
        var trimmed = reference.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            try
            {
                var byId = collection.GetById(id);
                if (byId is not null)
                {
                    return byId;
                }
            }
            catch
            {
                // ignored
            }
        }

        for (var i = 0; i < collection.Count; i++)
        {
            var entity = collection.GetByIndex(i);
            if (string.Equals(entity.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return entity;
            }
        }

        throw new RengaException(
            $"Unknown {label} '{reference}'. Use renga_list_drawing_resources to inspect available values.");
    }

    private static Dictionary<string, object?>[] DescribeEntities(IEntityCollection collection)
    {
        var result = new List<Dictionary<string, object?>>();
        for (var i = 0; i < collection.Count; i++)
        {
            var entity = collection.GetByIndex(i);
            result.Add(new Dictionary<string, object?>
            {
                ["id"] = entity.Id,
                ["name"] = entity.Name
            });
        }

        return result.ToArray();
    }

    private static int? GetIntParameter(IParameterContainer container, string parameterName)
    {
        try
        {
            var parameter = FindParameter(container, parameterName);
            if (parameter is null ||
                parameter.ValueType != ParameterValueType.ParameterValueType_Int)
            {
                return null;
            }

            return parameter.GetIntValue();
        }
        catch
        {
            return null;
        }
    }

    private static double? GetDoubleParameter(IParameterContainer container, string parameterName)
    {
        try
        {
            var parameter = FindParameter(container, parameterName);
            if (parameter is null ||
                parameter.ValueType != ParameterValueType.ParameterValueType_Double)
            {
                return null;
            }

            return parameter.GetDoubleValue();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringParameter(IParameterContainer container, string parameterName)
    {
        try
        {
            var parameter = FindParameter(container, parameterName);
            if (parameter is null ||
                parameter.ValueType != ParameterValueType.ParameterValueType_String)
            {
                return null;
            }

            return parameter.GetStringValue();
        }
        catch
        {
            return null;
        }
    }

    private static void SetContainerIntByName(
        IParameterContainer container,
        string parameterName,
        int value)
    {
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException($"Parameter '{parameterName}' is not available.");
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter '{parameterName}' is read-only.");
        }

        parameter.SetIntValue(value);
    }

    private static void SetContainerDoubleByName(
        IParameterContainer container,
        string parameterName,
        double value)
    {
        var parameter = FindParameter(container, parameterName)
            ?? throw new RengaException($"Parameter '{parameterName}' is not available.");
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter '{parameterName}' is read-only.");
        }

        parameter.SetDoubleValue(value);
    }

    private static int ParsePageOrientation(string orientation) =>
        orientation.Trim().ToLowerInvariant() switch
        {
            "portrait" or "книжная" => 1,
            "landscape" or "альбомная" => 0,
            _ => throw new RengaException("Page orientation must be portrait or landscape.")
        };

    private static string NormalizeDrawingFormat(string? format)
    {
        var normalized = string.IsNullOrWhiteSpace(format)
            ? "pdf"
            : format.Trim().ToLowerInvariant();
        return normalized switch
        {
            "pdf" => "pdf",
            "dxf" => "dxf",
            "dwg" => "dwg",
            "xps" or "openxps" => "xps",
            _ => throw new RengaException("Drawing format must be pdf, dxf, dwg, or xps.")
        };
    }

    private static string DrawingFormatExtension(string format) => format;

    private static AutocadVersion ParseAutocadVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return AutocadVersion.AutocadVersion_v2018;
        }

        var normalized = version.Trim().ToLowerInvariant().TrimStart('v', 'r');
        return normalized switch
        {
            "2000" => AutocadVersion.AutocadVersion_v2000,
            "2004" => AutocadVersion.AutocadVersion_v2004,
            "2007" => AutocadVersion.AutocadVersion_v2007,
            "2010" => AutocadVersion.AutocadVersion_v2010,
            "2013" => AutocadVersion.AutocadVersion_v2013,
            "2018" => AutocadVersion.AutocadVersion_v2018,
            _ => throw new RengaException(
                "AutoCAD version must be v2000, v2004, v2007, v2010, v2013, or v2018.")
        };
    }

    private static int ExportDrawingFile(
        IDrawing drawing,
        string path,
        string format,
        AutocadVersion version) => format switch
    {
        "pdf" => drawing.ExportToPdf(path, true),
        "dxf" => drawing.ExportToDxf(path, version, true),
        "dwg" => drawing.ExportToDwg(path, version, true),
        "xps" => drawing.ExportToOpenXps(path, true),
        _ => throw new RengaException($"Unsupported drawing format '{format}'.")
    };

    private static string EnsureOutputPath(string path, string extension)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RengaException("Output path must be non-empty.");
        }

        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), "." + extension, StringComparison.OrdinalIgnoreCase))
        {
            fullPath = Path.ChangeExtension(fullPath, extension);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "drawing" : sanitized;
    }

    private static bool IsLevel(IModelObject obj) => obj.ObjectType == RengaIds.Level;

    private static bool IsAxis(IModelObject obj) => obj.ObjectType == RengaIds.Axis;

    private static IModelObject? FindObject(IModel model, int objectId)
    {
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (obj.Id == objectId)
            {
                return obj;
            }
        }

        return null;
    }

    private static (double X, double Y) GetWallDirection(IModelObject wall)
    {
        if (wall is not ILevelObject levelObject)
        {
            throw new RengaException($"Wall {wall.Id} does not expose ILevelObject.");
        }

        var placement = levelObject.GetPlacement();
        var x = placement.AxisX.X;
        var y = placement.AxisX.Y;
        var length = Math.Sqrt(x * x + y * y);
        if (length < 1e-9)
        {
            throw new RengaException($"Wall {wall.Id} has an invalid placement direction.");
        }

        return (x / length, y / length);
    }

    private static IModelObject? FindLevel(IModel model, int levelId)
    {
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (obj.Id == levelId && IsLevel(obj))
            {
                return obj;
            }
        }

        return null;
    }

    private ResolvedLevel ResolveLevel(IModel model, int? levelId, double? elevationMeters)
    {
        if (levelId is int id)
        {
            var found = FindLevel(model, id)
                ?? throw new RengaException($"Level with ID {id} was not found.");
            var foundLevel = (ILevel)found;
            return new ResolvedLevel(found, found.Id, foundLevel.LevelName, foundLevel.Elevation);
        }

        var levels = new List<(IModelObject Obj, double ElevationMm)>();
        var objects = model.GetObjects();
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects.GetByIndex(i);
            if (obj is ILevel level)
            {
                levels.Add((obj, level.Elevation));
            }
        }

        if (levels.Count == 0)
        {
            throw new RengaException("No levels exist. Create a level first.");
        }

        IModelObject chosen;
        if (elevationMeters is double elevation)
        {
            var target = Units.ToMillimeters(elevation);
            chosen = levels.OrderBy(item => Math.Abs(item.ElevationMm - target)).First().Obj;
        }
        else
        {
            chosen = levels.OrderBy(item => item.ElevationMm).First().Obj;
        }

        var chosenLevel = (ILevel)chosen;
        return new ResolvedLevel(chosen, chosen.Id, chosenLevel.LevelName, chosenLevel.Elevation);
    }

    private static Dictionary<string, object?> DescribeProject(IProject project, IModel model)
    {
        string filePath;
        try
        {
            filePath = project.FilePath ?? string.Empty;
        }
        catch
        {
            filePath = string.Empty;
        }

        return new Dictionary<string, object?>
        {
            ["filePath"] = filePath,
            ["projectType"] = ProjectTypeName(project.ProjectType),
            ["modelId"] = model.Id.ToString("B"),
            ["objectCount"] = model.GetObjects().Count,
            ["hasGeometry"] = HasGeometry(model)
        };
    }

    private static string ProjectTypeName(ProjectType projectType) => projectType switch
    {
        ProjectType.ProjectType_Project => "Project",
        ProjectType.ProjectType_Template => "Template",
        _ => ((int)projectType).ToString(CultureInfo.InvariantCulture)
    };

    private static Dictionary<string, object?> ToDictionary(LevelInfo level) => new()
    {
        ["id"] = level.Id,
        ["uniqueId"] = level.UniqueId,
        ["name"] = level.Name,
        ["elevation"] = Units.ToMeters(level.ElevationMm),
        ["elevationMm"] = level.ElevationMm,
        ["originZ"] = Units.ToMeters(level.OriginZMm)
    };

    private static Dictionary<string, object?> ToDictionary(AxisInfo axis) => new()
    {
        ["id"] = axis.Id,
        ["uniqueId"] = axis.UniqueId,
        ["name"] = axis.Name
    };

    private static Dictionary<string, object?> ToDictionary(SeedLevel level) => new()
    {
        ["name"] = level.Name,
        ["elevation"] = Units.ToMeters(level.ElevationMm)
    };

    private static Dictionary<string, object?> ToDictionary(SeedAxis axis) => new()
    {
        ["name"] = axis.Name,
        ["x1"] = Units.ToMeters(axis.X1),
        ["y1"] = Units.ToMeters(axis.Y1),
        ["x2"] = Units.ToMeters(axis.X2),
        ["y2"] = Units.ToMeters(axis.Y2)
    };

    private static string RequireName(string name, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RengaException($"Parameter '{parameterName}' must be non-empty.");
        }

        return name.Trim();
    }

    private static double RequireFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new RengaException($"Parameter '{parameterName}' must be a finite number.");
        }

        return value;
    }

    private static Dictionary<string, object?> DescribeObject(IModelObject obj)
    {
        var result = new Dictionary<string, object?>
        {
            ["id"] = obj.Id,
            ["uniqueId"] = obj.UniqueIdS,
            ["typeId"] = obj.ObjectTypeS,
            ["typeName"] = TypeName(obj.ObjectType),
            ["name"] = obj.Name
        };

        if (obj is ILevel level)
        {
            result["levelName"] = level.LevelName;
            result["elevation"] = Units.ToMeters(level.Elevation);
        }

        if (obj is ILevelObject levelObject)
        {
            result["levelId"] = levelObject.LevelId;
            result["elevationAboveLevel"] = Units.ToMeters(levelObject.ElevationAboveLevel);
        }

        if (obj is IHostObject host)
        {
            try
            {
                result["dependentIds"] = host.GetDependentObjectIds();
            }
            catch
            {
                // ignored
            }
        }

        return result;
    }

    private static Dictionary<string, object?>[] DescribeParameters(IModelObject obj, IProject project)
    {
        IParameterContainer? container;
        try
        {
            container = obj.GetParameters();
        }
        catch
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        return container is null
            ? Array.Empty<Dictionary<string, object?>>()
            : DescribeContainerParameters(container, project);
    }

    private static Dictionary<string, object?>[] DescribeContainerParameters(
        IParameterContainer container,
        IProject project)
    {
        var result = new List<Dictionary<string, object?>>();
        var ids = container.GetIds();
        for (var i = 0; i < ids.Count; i++)
        {
            IParameter parameter;
            try
            {
                parameter = container.Get(ids.Get(i));
            }
            catch
            {
                continue;
            }

            var definition = parameter.Definition;
            var parameterType = definition?.ParameterType ?? ParameterType.ParameterType_Undefined;
            var rawValue = GetRawParameterValue(parameter);
            var (value, unit) = ToHumanParameterValue(rawValue, parameterType);
            var referencedName = rawValue is int referencedId
                ? ResolveReferencedName(project, referencedId, definition?.Name ?? string.Empty)
                : null;

            string[] enumeration;
            try
            {
                enumeration = definition?.EnumerationItemTexts ?? Array.Empty<string>();
            }
            catch
            {
                enumeration = Array.Empty<string>();
            }

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = parameter.IdS,
                ["name"] = definition?.Name,
                ["text"] = definition?.Text,
                ["group"] = definition?.GroupText,
                ["parameterType"] = ParameterTypeName(parameterType),
                ["valueType"] = ValueTypeName(parameter.ValueType),
                ["hasValue"] = parameter.HasValue,
                ["readOnly"] = parameter.IsReadOnly,
                ["rawValue"] = rawValue,
                ["value"] = value,
                ["unit"] = unit,
                ["referencedEntityType"] = definition?.ReferencedEntityTypeS,
                ["referencedName"] = referencedName,
                ["enumeration"] = enumeration
            });
        }

        return result.ToArray();
    }

    private static Dictionary<string, object?>[] DescribeProperties(IModelObject obj)
    {
        var result = new List<Dictionary<string, object?>>();
        IPropertyContainer container;
        try
        {
            container = obj.GetProperties();
        }
        catch
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        if (container is null)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var ids = container.GetIds();
        for (var i = 0; i < ids.Count; i++)
        {
            IProperty property;
            try
            {
                property = container.Get(ids.Get(i));
            }
            catch
            {
                continue;
            }

            var hasValue = false;
            try
            {
                hasValue = property.HasValue();
            }
            catch
            {
                // ignored
            }

            object? value = null;
            string? unit = null;
            if (hasValue)
            {
                try
                {
                    (value, unit) = GetPropertyValue(property);
                }
                catch
                {
                    // ignored
                }
            }

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = property.IdS,
                ["name"] = property.Name,
                ["type"] = PropertyTypeName(property.Type),
                ["hasValue"] = hasValue,
                ["value"] = value,
                ["unit"] = unit
            });
        }

        return result.ToArray();
    }

    private static (object? Value, string? Unit) GetPropertyValue(IProperty property) => property.Type switch
    {
        PropertyType.PropertyType_Double => (property.GetDoubleValue(), null),
        PropertyType.PropertyType_String => (property.GetStringValue(), null),
        PropertyType.PropertyType_Angle => (property.GetAngleValue(AngleUnit.AngleUnit_Degrees), "deg"),
        PropertyType.PropertyType_Area => (property.GetAreaValue(AreaUnit.AreaUnit_Meters2), "m2"),
        PropertyType.PropertyType_Boolean => (property.GetBooleanValue(), null),
        PropertyType.PropertyType_Enumeration => (property.GetEnumerationValue(), null),
        PropertyType.PropertyType_Integer => (property.GetIntegerValue(), null),
        PropertyType.PropertyType_Length => (property.GetLengthValue(LengthUnit.LengthUnit_Meters), "m"),
        PropertyType.PropertyType_Logical => (property.GetLogicalValue().ToString(), null),
        PropertyType.PropertyType_Mass => (property.GetMassValue(MassUnit.MassUnit_Kilograms), "kg"),
        PropertyType.PropertyType_Volume => (property.GetVolumeValue(VolumeUnit.VolumeUnit_Meters3), "m3"),
        _ => (null, null)
    };

    private static object? GetRawParameterValue(IParameter parameter) => parameter.ValueType switch
    {
        ParameterValueType.ParameterValueType_Bool => parameter.GetBoolValue(),
        ParameterValueType.ParameterValueType_Int => parameter.GetIntValue(),
        ParameterValueType.ParameterValueType_Double => parameter.GetDoubleValue(),
        ParameterValueType.ParameterValueType_String => parameter.GetStringValue(),
        _ => null
    };

    private static (object? Value, string? Unit) ToHumanParameterValue(
        object? rawValue,
        ParameterType parameterType)
    {
        if (rawValue is not double number)
        {
            return (rawValue, null);
        }

        return parameterType switch
        {
            ParameterType.ParameterType_Length => (number / Units.MillimetersPerMeter, "m"),
            ParameterType.ParameterType_Elevation => (number / Units.MillimetersPerMeter, "m"),
            ParameterType.ParameterType_Angle => (number, "deg"),
            ParameterType.ParameterType_Area => (number, "m2"),
            ParameterType.ParameterType_Volume => (number, "m3"),
            ParameterType.ParameterType_Mass => (number, "kg"),
            ParameterType.ParameterType_Temperature => (number, "C"),
            ParameterType.ParameterType_Density => (number, "kg/m3"),
            ParameterType.ParameterType_ThermalConductivity => (number, "W/mK"),
            ParameterType.ParameterType_Voltage => (number, "V"),
            ParameterType.ParameterType_Pressure => (number, "Pa"),
            _ => (number, null)
        };
    }

    private static IParameter? FindParameter(IParameterContainer container, string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var guid))
        {
            return container.Contains(guid) ? container.Get(guid) : null;
        }

        var normalized = nameOrId.Trim().Trim('{', '}');
        var ids = container.GetIds();
        for (var i = 0; i < ids.Count; i++)
        {
            var parameter = container.Get(ids.Get(i));
            var definition = parameter.Definition;
            if (string.Equals(definition?.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(definition?.Text, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parameter.IdS.Trim().Trim('{', '}'), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return parameter;
            }
        }

        return null;
    }

    private static object ParseJsonParameterValue(JsonElement value, IParameter parameter)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new RengaException("Parameter value must not be null.");
        }

        return parameter.ValueType switch
        {
            ParameterValueType.ParameterValueType_Bool => ParseBool(value),
            ParameterValueType.ParameterValueType_Int => ParseInt(value),
            ParameterValueType.ParameterValueType_Double => ParseDouble(value),
            ParameterValueType.ParameterValueType_String => ParseString(value),
            _ => throw new RengaException("Unsupported parameter value type.")
        };
    }

    private static object ParseBool(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String &&
            bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        throw new RengaException("Boolean parameter requires true or false.");
    }

    private static object ParseInt(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        throw new RengaException("Integer parameter requires an integer or a name.");
    }

    private static object ParseDouble(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new RengaException("Double parameter requires a finite number.");
    }

    private static object ParseString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new RengaException("String parameter requires a string value.")
        };
    }

    private object ApplyParameterValue(
        IProject project,
        int objectId,
        string objectType,
        IParameter parameter,
        object value,
        string? unit)
    {
        if (parameter.IsReadOnly)
        {
            throw new RengaException(
                $"Parameter '{parameter.Definition?.Name ?? parameter.IdS}' is read-only.");
        }

        var definition = parameter.Definition;
        var parameterType = definition?.ParameterType ?? ParameterType.ParameterType_Undefined;
        var oldRaw = GetRawParameterValue(parameter);
        var (oldValue, oldUnit) = ToHumanParameterValue(oldRaw, parameterType);

        var op = StartOperation(project);
        var applied = false;
        try
        {
            switch (parameter.ValueType)
            {
                case ParameterValueType.ParameterValueType_Bool:
                    parameter.SetBoolValue(ToBoolean(value));
                    break;
                case ParameterValueType.ParameterValueType_Int:
                    parameter.SetIntValue(ToInt32(project, parameter, value));
                    break;
                case ParameterValueType.ParameterValueType_Double:
                    parameter.SetDoubleValue(ToRawDouble(ToDouble(value), parameterType, unit));
                    break;
                case ParameterValueType.ParameterValueType_String:
                    parameter.SetStringValue(ToStringValue(value, definition));
                    break;
                default:
                    throw new RengaException("Unsupported parameter value type.");
            }

            op.Apply();
            applied = true;
        }
        catch
        {
            if (!applied)
            {
                TryRollback(op);
            }

            throw;
        }

        var newRaw = GetRawParameterValue(parameter);
        var (newValue, newUnit) = ToHumanParameterValue(newRaw, parameterType);

        return new Dictionary<string, object?>
        {
            ["updated"] = true,
            ["objectId"] = objectId,
            ["objectType"] = objectType,
            ["parameterId"] = parameter.IdS,
            ["parameterName"] = definition?.Name,
            ["parameterType"] = ParameterTypeName(parameterType),
            ["valueType"] = ValueTypeName(parameter.ValueType),
            ["oldRawValue"] = oldRaw,
            ["oldValue"] = oldValue,
            ["oldUnit"] = oldUnit,
            ["newRawValue"] = newRaw,
            ["newValue"] = newValue,
            ["newUnit"] = newUnit
        };
    }

    private static bool ToBoolean(object value)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is string text)
        {
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (text == "1")
            {
                return true;
            }

            if (text == "0")
            {
                return false;
            }
        }

        throw new RengaException("Boolean parameter value is invalid.");
    }

    private static double ToDouble(object value)
    {
        return value switch
        {
            double number => number,
            int number => number,
            long number => number,
            float number => number,
            string text when double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw new RengaException("Double parameter value is invalid.")
        };
    }

    private static int ToInt32(IProject project, IParameter parameter, object value)
    {
        if (value is int number)
        {
            return number;
        }

        if (value is long longNumber)
        {
            return checked((int)longNumber);
        }

        if (value is double doubleNumber)
        {
            return checked((int)doubleNumber);
        }

        if (value is string text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            var resolved = ResolveEntityIdByName(
                project,
                parameter.Definition?.Name ?? string.Empty,
                text);
            if (resolved is int id)
            {
                return id;
            }
        }

        throw new RengaException("Integer parameter value is invalid.");
    }

    private static string ToStringValue(object value, IParameterDefinition? definition)
    {
        var text = value switch
        {
            string stringValue => stringValue,
            bool boolean => boolean ? "true" : "false",
            int number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            double number => number.ToString(CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        string[] enumeration;
        try
        {
            enumeration = definition?.EnumerationItemTexts ?? Array.Empty<string>();
        }
        catch
        {
            enumeration = Array.Empty<string>();
        }

        if (enumeration.Length > 0 &&
            !enumeration.Any(item => string.Equals(item, text, StringComparison.Ordinal)))
        {
            throw new RengaException(
                $"Value '{text}' is not allowed. Available values: {string.Join(", ", enumeration)}.");
        }

        return text;
    }

    private static double ToRawDouble(double value, ParameterType parameterType, string? unit)
    {
        var normalized = unit?.Trim().ToLowerInvariant();
        return parameterType switch
        {
            ParameterType.ParameterType_Length or ParameterType.ParameterType_Elevation => normalized switch
            {
                null or "" or "m" => value * Units.MillimetersPerMeter,
                "mm" => value,
                "cm" => value * 10,
                "raw" => value,
                _ => throw new RengaException("Length unit must be m, mm, cm, or raw.")
            },
            ParameterType.ParameterType_Angle => normalized switch
            {
                null or "" or "deg" => value,
                "rad" => value * 180.0 / Math.PI,
                "raw" => value,
                _ => throw new RengaException("Angle unit must be deg, rad, or raw.")
            },
            ParameterType.ParameterType_Area => normalized switch
            {
                null or "" or "m2" => value,
                "cm2" => value / 10_000.0,
                "mm2" => value / 1_000_000.0,
                "raw" => value,
                _ => throw new RengaException("Area unit must be m2, cm2, mm2, or raw.")
            },
            ParameterType.ParameterType_Volume => normalized switch
            {
                null or "" or "m3" => value,
                "cm3" => value / 1_000_000.0,
                "mm3" => value / 1_000_000_000.0,
                "raw" => value,
                _ => throw new RengaException("Volume unit must be m3, cm3, mm3, or raw.")
            },
            ParameterType.ParameterType_Mass => normalized switch
            {
                null or "" or "kg" => value,
                "g" => value / 1_000.0,
                "t" => value * 1_000.0,
                "raw" => value,
                _ => throw new RengaException("Mass unit must be kg, g, t, or raw.")
            },
            _ => value
        };
    }

    private static string? ResolveReferencedName(IProject project, int id, string parameterName)
    {
        try
        {
            if (parameterName.Contains("LayeredMaterial", StringComparison.OrdinalIgnoreCase))
            {
                return FindEntityName(project.LayeredMaterials, id);
            }

            if (parameterName.EndsWith("MaterialId", StringComparison.OrdinalIgnoreCase))
            {
                return FindEntityName(project.Materials, id);
            }

            if (string.Equals(parameterName, "LevelId", StringComparison.OrdinalIgnoreCase))
            {
                var level = FindObject(project.Model, id);
                return level is ILevel nativeLevel ? nativeLevel.LevelName : null;
            }

            var collection = parameterName switch
            {
                "DoorStyleId" => project.DoorStyles,
                "WindowStyleId" => project.WindowStyles,
                "BeamStyleId" => project.BeamStyles,
                "ColumnStyleId" => project.ColumnStyles,
                "PlateStyleId" => project.PlateStyles,
                "ElementStyleId" => project.ElementStyles,
                "SystemStyleId" => project.SystemStyles,
                _ => null
            };

            return collection is null ? null : FindEntityName(collection, id);
        }
        catch
        {
            return null;
        }
    }

    private static int? ResolveEntityIdByName(IProject project, string parameterName, string name)
    {
        try
        {
            IEntityCollection? collection = null;
            if (parameterName.Contains("LayeredMaterial", StringComparison.OrdinalIgnoreCase))
            {
                collection = project.LayeredMaterials;
            }
            else if (parameterName.EndsWith("MaterialId", StringComparison.OrdinalIgnoreCase))
            {
                collection = project.Materials;
            }
            else
            {
                collection = parameterName switch
                {
                    "DoorStyleId" => project.DoorStyles,
                    "WindowStyleId" => project.WindowStyles,
                    "BeamStyleId" => project.BeamStyles,
                    "ColumnStyleId" => project.ColumnStyles,
                    "PlateStyleId" => project.PlateStyles,
                    "ElementStyleId" => project.ElementStyles,
                    "SystemStyleId" => project.SystemStyles,
                    _ => null
                };
            }

            if (collection is null)
            {
                return null;
            }

            for (var i = 0; i < collection.Count; i++)
            {
                var entity = collection.GetByIndex(i);
                if (string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entity.Id;
                }
            }
        }
        catch
        {
            // ignored
        }

        return null;
    }

    private static string? FindEntityName(IEntityCollection collection, int id)
    {
        try
        {
            var entity = collection.GetById(id);
            return entity?.Name;
        }
        catch
        {
            return null;
        }
    }

    private static IEntity? FindEntityById(IEntityCollection collection, int id)
    {
        try
        {
            return collection.GetById(id);
        }
        catch
        {
            return null;
        }
    }

    private static IParameterContainer GetEntityContainer(IEntity entity)
    {
        var container = entity as IParameterContainer
            ?? entity.GetInterfaceByName("IParameterContainer") as IParameterContainer;
        return container
            ?? throw new RengaException($"Entity {entity.Id} does not expose IParameterContainer.");
    }

    private static void SetContainerInt(IParameterContainer container, Guid parameterId, int value)
    {
        if (!container.Contains(parameterId))
        {
            throw new RengaException($"Parameter {parameterId:B} is not available.");
        }

        var parameter = container.Get(parameterId);
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter {parameterId:B} is read-only.");
        }

        parameter.SetIntValue(value);
    }

    private static IEntity FindStyleEntity(IProject project, string kind, int styleId)
    {
        var normalized = kind.Trim().ToLowerInvariant();
        if (!StyleCollections.TryGetValue(normalized, out var getter))
        {
            throw new RengaException(
                $"Unknown style kind '{kind}'. Allowed values: {string.Join(", ", StyleCollections.Keys)}.");
        }

        return getter(project).GetById(styleId)
            ?? throw new RengaException($"Style {styleId} was not found in '{normalized}'.");
    }

    private static Guid ResolveObjectTypeGuid(string objectType)
    {
        if (Guid.TryParse(objectType, out var guid))
        {
            return guid;
        }

        if (FriendlyTypeNames.TryGetValue(objectType.Trim(), out var known))
        {
            return known;
        }

        throw new RengaException(
            $"Unknown object type '{objectType}'. Use a GUID or one of: {string.Join(", ", FriendlyTypeNames.Keys)}.");
    }

    private static PropertyType ParsePropertyType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "double" => PropertyType.PropertyType_Double,
        "string" => PropertyType.PropertyType_String,
        "angle" => PropertyType.PropertyType_Angle,
        "area" => PropertyType.PropertyType_Area,
        "boolean" or "bool" => PropertyType.PropertyType_Boolean,
        "enumeration" or "enum" => PropertyType.PropertyType_Enumeration,
        "integer" or "int" => PropertyType.PropertyType_Integer,
        "length" => PropertyType.PropertyType_Length,
        "logical" => PropertyType.PropertyType_Logical,
        "mass" => PropertyType.PropertyType_Mass,
        "volume" => PropertyType.PropertyType_Volume,
        _ => throw new RengaException(
            "Property type must be one of: string, double, length, angle, area, boolean, enumeration, integer, logical, mass, volume.")
    };

    private static int ToInteger(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new RengaException("Integer property requires an integer.");
    }

    private static void ValidatePropertyEnumeration(IProject project, Guid propertyId, string value)
    {
        string[] items;
        try
        {
            items = project.PropertyManager.GetPropertyDescription2(propertyId)?.GetEnumerationItems()
                ?? Array.Empty<string>();
        }
        catch
        {
            items = Array.Empty<string>();
        }

        if (items.Length > 0 &&
            !items.Any(item => string.Equals(item, value, StringComparison.Ordinal)))
        {
            throw new RengaException(
                $"Value '{value}' is not allowed. Available values: {string.Join(", ", items)}.");
        }
    }

    private static void SetContainerString(IParameterContainer container, Guid parameterId, string value)
    {
        if (!container.Contains(parameterId))
        {
            throw new RengaException($"Parameter {parameterId:B} is not available.");
        }

        var parameter = container.Get(parameterId);
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter {parameterId:B} is read-only.");
        }

        parameter.SetStringValue(value);
    }

    private static void SetContainerDouble(IParameterContainer container, Guid parameterId, double value)
    {
        if (!container.Contains(parameterId))
        {
            throw new RengaException($"Parameter {parameterId:B} is not available.");
        }

        var parameter = container.Get(parameterId);
        if (parameter.IsReadOnly)
        {
            throw new RengaException($"Parameter {parameterId:B} is read-only.");
        }

        parameter.SetDoubleValue(value);
    }

    private static string TypeName(Guid type)
    {
        foreach (var pair in FriendlyTypeNames)
        {
            if (pair.Value == type)
            {
                return pair.Key;
            }
        }

        return type.ToString("B");
    }

    private static string ParameterTypeName(ParameterType type)
    {
        var text = type.ToString();
        return text.StartsWith("ParameterType_", StringComparison.Ordinal)
            ? text["ParameterType_".Length..]
            : text;
    }

    private static string ValueTypeName(ParameterValueType type)
    {
        var text = type.ToString();
        return text.StartsWith("ParameterValueType_", StringComparison.Ordinal)
            ? text["ParameterValueType_".Length..]
            : text;
    }

    private static string PropertyTypeName(PropertyType type)
    {
        var text = type.ToString();
        return text.StartsWith("PropertyType_", StringComparison.Ordinal)
            ? text["PropertyType_".Length..]
            : text;
    }

    private static string QuantityTypeName(QuantityType type)
    {
        var text = type.ToString();
        return text.StartsWith("QuantityType_", StringComparison.Ordinal)
            ? text["QuantityType_".Length..]
            : text;
    }

    private static readonly Dictionary<string, Guid> StyleTypeIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["door"] = RengaIds.DoorStyle,
        ["window"] = RengaIds.WindowStyle,
        ["beam"] = RengaIds.BeamStyle,
        ["column"] = RengaIds.ColumnStyle,
        ["plate"] = RengaIds.PlateStyle
    };

    private static readonly Dictionary<string, Guid> FriendlyTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["level"] = RengaIds.Level,
        ["axis"] = RengaIds.Axis,
        ["wall"] = RengaIds.Wall,
        ["column"] = RengaIds.Column,
        ["beam"] = RengaIds.Beam,
        ["floor"] = RengaIds.Floor,
        ["roof"] = RengaIds.Roof,
        ["door"] = RengaIds.Door,
        ["window"] = RengaIds.Window,
        ["opening"] = RengaIds.Opening,
        ["plate"] = RengaIds.Plate,
        ["room"] = RengaIds.Room,
        ["stair"] = RengaIds.Stair,
        ["railing"] = RengaIds.Railing,
        ["ramp"] = RengaIds.Ramp,
        ["wallfoundation"] = RengaIds.WallFoundation,
        ["isolatedfoundation"] = RengaIds.IsolatedFoundation,
        ["element"] = RengaIds.Element,
        ["material"] = RengaIds.Material,
        ["line3d"] = RengaIds.Line3D,
        ["pipe"] = RengaIds.Pipe,
        ["duct"] = RengaIds.Duct,
        ["electrical_circuit_line"] = RengaIds.ElectricalCircuitLine,
        ["pipe_fitting"] = RengaIds.PipeFitting,
        ["pipe_accessory"] = RengaIds.PipeAccessory,
        ["duct_fitting"] = RengaIds.DuctFitting,
        ["duct_accessory"] = RengaIds.DuctAccessory,
        ["route"] = RengaIds.Route,
        ["route_point"] = RengaIds.RoutePoint,
        ["equipment"] = RengaIds.Equipment,
        ["mechanical_equipment"] = RengaIds.MechanicalEquipment,
        ["plumbing_fixture"] = RengaIds.PlumbingFixture,
        ["lighting_fixture"] = RengaIds.LightingFixture,
        ["wiring_accessory"] = RengaIds.WiringAccessory,
        ["electric_distribution_board"] = RengaIds.ElectricDistributionBoard
    };

    private static readonly Dictionary<string, Func<IProject, IEntityCollection>> StyleCollections =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["beam"] = project => project.BeamStyles,
            ["column"] = project => project.ColumnStyles,
            ["door"] = project => project.DoorStyles,
            ["window"] = project => project.WindowStyles,
            ["plate"] = project => project.PlateStyles,
            ["element"] = project => project.ElementStyles,
            ["equipment"] = project => project.EquipmentStyles,
            ["mechanical_equipment"] = project => project.MechanicalEquipmentStyles,
            ["plumbing_fixture"] = project => project.PlumbingFixtureStyles,
            ["lighting_fixture"] = project => project.LightingFixtureStyles,
            ["wiring_accessory"] = project => project.WiringAccessoryStyles,
            ["electric_distribution_board"] = project => project.ElectricDistributionBoardStyles,
            ["pipe"] = project => project.PipeStyles,
            ["pipe_fitting"] = project => project.PipeFittingStyles,
            ["pipe_accessory"] = project => project.PipeAccessoryStyles,
            ["duct"] = project => project.DuctStyles,
            ["duct_fitting"] = project => project.DuctFittingStyles,
            ["duct_accessory"] = project => project.DuctAccessoryStyles,
            ["electrical_circuit_line"] = project => project.ElectricalCircuitLineStyles,
            ["system"] = project => project.SystemStyles,
            ["profile"] = project => project.Profiles,
            ["material"] = project => project.Materials,
            ["layered_material"] = project => project.LayeredMaterials
        };

    private sealed record ResolvedLevel(IModelObject Obj, int Id, string Name, double ElevationMm);

    private sealed class SeedState
    {
        public List<SeedLevel> Levels { get; set; } = new();
        public List<SeedAxis> Axes { get; set; } = new();
        public bool AxesLocked { get; set; }
        public bool FreshDefault { get; set; }
        public bool Managed { get; set; }
    }
}







