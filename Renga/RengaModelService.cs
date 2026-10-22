using System.Globalization;
using System.Text;
using System.Text.Json;
using Renga;

namespace RengaMcpServer.Renga;

public sealed class RengaModelService
{
    private readonly RengaConnector _connector;
    private SeedState _state = new();
    private Guid _stateModelId = Guid.Empty;

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
        EnsureState(model);

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
        _stateModelId = Guid.Empty;
        var project = _connector.RequireProject();
        var model = project.Model;
        EnsureState(model);
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

        _stateModelId = Guid.Empty;
        var project = app.Project;
        var model = project.Model;
        EnsureState(model);

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
        EnsureState(model);

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
        EnsureState(model);

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
        return ApplyParameterValue(project, obj, parameter, clrValue, unit);
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
            obj,
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
            _stateModelId = importedModel.Id;
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

    private void EnsureState(IModel model)
    {
        var modelId = model.Id;
        if (_stateModelId == modelId)
        {
            return;
        }

        _stateModelId = modelId;
        _state = new SeedState();

        var levels = ReadLevels(model);
        _state.Levels = levels.Select(level => new SeedLevel(level.Name, level.ElevationMm)).ToList();

        var axes = ReadAxes(model);
        _state.AxesLocked = axes.Count > 0;
        _state.Managed = false;
        _state.FreshDefault = false;
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
        var result = new List<Dictionary<string, object?>>();
        IParameterContainer? container;
        try
        {
            container = obj.GetParameters();
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
        IModelObject obj,
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
            ["objectId"] = obj.Id,
            ["objectType"] = obj.ObjectTypeS,
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
        ["line3d"] = RengaIds.Line3D
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





