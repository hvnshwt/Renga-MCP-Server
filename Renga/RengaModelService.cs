using System.Globalization;
using System.Text;
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
        double heightMeters)
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
        double? elevationMeters)
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

            var styleId = FirstStyleId(project.ColumnStyles);
            if (styleId > 0)
            {
                args.StyleId = styleId;
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
                ["elevationAboveLevel"] = Units.ToMeters(levelObject?.ElevationAboveLevel ?? localZ)
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
        int? levelId)
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

            var styleId = FirstStyleId(project.BeamStyles);
            if (styleId > 0)
            {
                args.StyleId = styleId;
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
                ["styleId"] = styleId
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
        double? offsetMeters)
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
            args.StyleId = 0;
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
                ["offset"] = Units.ToMeters(offsetMm)
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
        double? sillMeters)
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
            args.StyleId = 0;
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
                ["sill"] = Units.ToMeters(sillMm)
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

