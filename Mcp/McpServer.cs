using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using RengaMcpServer.Renga;

namespace RengaMcpServer.Mcp;

public sealed class McpServer
{
    private static readonly JsonSerializerOptions ProtocolJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly RengaModelService _service;
    private readonly StreamReader _input;
    private readonly StreamWriter _output;

    public McpServer(RengaModelService service)
    {
        _service = service;
        _input = new StreamReader(
            Console.OpenStandardInput(),
            new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: false);
        _output = new StreamWriter(
            Console.OpenStandardOutput(),
            new UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\n"
        };
    }

    public void Run()
    {
        Log("MCP server is listening on stdio");
        string? message;
        while ((message = ReadMessage()) is not null)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            Handle(message);
        }

        Log("stdin closed; server exiting");
    }

    private void Handle(string message)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(message);
        }
        catch (JsonException ex)
        {
            SendError(null, -32700, "Parse error", ex.Message);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                SendError(null, -32600, "Invalid Request");
                return;
            }

            var hasId = root.TryGetProperty("id", out var id) && id.ValueKind != JsonValueKind.Null;
            if (!root.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                if (hasId)
                {
                    SendError(id, -32600, "Invalid Request");
                }

                return;
            }

            var method = methodElement.GetString() ?? string.Empty;
            if (!hasId)
            {
                HandleNotification(method);
                return;
            }

            try
            {
                switch (method)
                {
                    case "initialize":
                        SendResult(id, Initialize(root));
                        break;
                    case "ping":
                        SendResult(id, new Dictionary<string, object?>());
                        break;
                    case "tools/list":
                        SendResult(id, ListTools());
                        break;
                    case "tools/call":
                        HandleToolCall(id, root);
                        break;
                    case "resources/list":
                        SendResult(id, new Dictionary<string, object?> { ["resources"] = Array.Empty<object>() });
                        break;
                    case "resources/templates/list":
                        SendResult(id, new Dictionary<string, object?> { ["resourceTemplates"] = Array.Empty<object>() });
                        break;
                    case "prompts/list":
                        SendResult(id, new Dictionary<string, object?> { ["prompts"] = Array.Empty<object>() });
                        break;
                    case "logging/setLevel":
                        SendResult(id, new Dictionary<string, object?>());
                        break;
                    default:
                        SendError(id, -32601, $"Method not found: {method}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log($"Request {method} failed: {ex}");
                SendError(id, -32603, "Internal error", ex.Message);
            }
        }
    }

    private void HandleNotification(string method)
    {
        if (string.Equals(method, "notifications/initialized", StringComparison.Ordinal))
        {
            Log("Client completed initialization");
            return;
        }

        Log($"Ignored notification: {method}");
    }

    private object Initialize(JsonElement root)
    {
        var protocolVersion = "2025-06-18";
        if (root.TryGetProperty("params", out var parameters) &&
            parameters.ValueKind == JsonValueKind.Object &&
            parameters.TryGetProperty("protocolVersion", out var version) &&
            version.ValueKind == JsonValueKind.String)
        {
            protocolVersion = version.GetString() ?? protocolVersion;
        }

        return new Dictionary<string, object?>
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["tools"] = new Dictionary<string, object?>
                {
                    ["listChanged"] = false
                }
            },
            ["serverInfo"] = new Dictionary<string, object?>
            {
                ["name"] = "renga-mcp-server",
                ["version"] = "1.0.0"
            },
            ["instructions"] =
                "Use renga_ping to check the connection and renga_new_project to start. " +
                "Create all levels and axes before creating walls, columns, beams, or floors. " +
                "Openings are hosted by floors and roofs; use doors and windows for wall apertures. " +
                "All user-facing coordinates, elevations, heights, and thicknesses are in meters."
        };
    }

    private object ListTools()
    {
        var tools = ToolCatalog.Tools.Select(tool => new Dictionary<string, object?>
        {
            ["name"] = tool.Name,
            ["title"] = tool.Title,
            ["description"] = tool.Description,
            ["inputSchema"] = ParseSchema(tool.SchemaJson)
        }).ToArray();

        return new Dictionary<string, object?> { ["tools"] = tools };
    }

    private static JsonElement ParseSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private void HandleToolCall(JsonElement id, JsonElement root)
    {
        if (!root.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            SendError(id, -32602, "Invalid params: tools/call requires a tool name");
            return;
        }

        var name = nameElement.GetString() ?? string.Empty;
        var tool = ToolCatalog.Tools.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (tool is null)
        {
            SendError(id, -32602, $"Unknown tool: {name}");
            return;
        }

        var arguments = parameters.TryGetProperty("arguments", out var args) &&
                        args.ValueKind == JsonValueKind.Object
            ? args
            : default;

        Log($"Calling {name}");
        try
        {
            var result = Dispatch(name, arguments);
            var text = JsonSerializer.Serialize(result, PrettyJson);
            SendResult(id, new Dictionary<string, object?>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = text
                    }
                },
                ["isError"] = false
            });
        }
        catch (Exception ex)
        {
            Log($"{name} failed: {ex}");
            var message = ex is RengaException
                ? ex.Message
                : $"{ex.GetType().Name}: {ex.Message}";
            SendResult(id, new Dictionary<string, object?>
            {
                ["content"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = message
                    }
                },
                ["isError"] = true
            });
        }
    }

    private object Dispatch(string name, JsonElement args) => name switch
    {
        "renga_ping" => _service.Ping(),
        "renga_new_project" => _service.NewProject(),
        "renga_open_project" => _service.OpenProject(RequireString(args, "path")),
        "renga_list_levels" => _service.ListLevels(),
        "renga_create_level" => _service.CreateLevel(
            RequireString(args, "name"),
            RequireDouble(args, "elevation")),
        "renga_create_axis" => _service.CreateAxis(
            RequireString(args, "name"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2")),
        "renga_create_wall" => _service.CreateWall(
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireInt(args, "levelId"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "thickness")),
        "renga_create_column" => _service.CreateColumn(
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "height"),
            OptionalInt(args, "levelId"),
            OptionalDouble(args, "elevation"),
            OptionalInt(args, "styleId")),
        "renga_create_beam" => _service.CreateBeam(
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "z1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "z2"),
            OptionalInt(args, "levelId"),
            OptionalInt(args, "styleId")),
        "renga_create_door" => _service.CreateDoor(
            RequireInt(args, "wallId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "width"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "offset"),
            OptionalInt(args, "styleId")),
        "renga_create_window" => _service.CreateWindow(
            RequireInt(args, "wallId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "width"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "sill"),
            OptionalInt(args, "styleId")),
        "renga_create_roof" => _service.CreateRoof(
            RequireInt(args, "levelId"),
            RequireDouble(args, "thickness"),
            OptionalPoints(args, "points"),
            OptionalDouble(args, "x1"),
            OptionalDouble(args, "y1"),
            OptionalDouble(args, "x2"),
            OptionalDouble(args, "y2"),
            OptionalDouble(args, "offset")),
        "renga_create_floor" => _service.CreateFloor(
            RequireInt(args, "levelId"),
            RequireDouble(args, "thickness"),
            OptionalPoints(args, "points"),
            OptionalDouble(args, "x1"),
            OptionalDouble(args, "y1"),
            OptionalDouble(args, "x2"),
            OptionalDouble(args, "y2")),
        "renga_create_opening" => _service.CreateOpening(
            RequireInt(args, "hostId"),
            OptionalPoints(args, "points"),
            OptionalDouble(args, "x1"),
            OptionalDouble(args, "y1"),
            OptionalDouble(args, "x2"),
            OptionalDouble(args, "y2"),
            OptionalDouble(args, "depth"),
            OptionalDouble(args, "offset"),
            OptionalString(args, "mark")),
        "renga_create_plate" => _service.CreatePlate(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            OptionalDouble(args, "thickness"),
            OptionalDouble(args, "elevation"),
            OptionalInt(args, "styleId"),
            OptionalInt(args, "materialId"),
            OptionalString(args, "mark")),
        "renga_create_wall_foundation" => _service.CreateWallFoundation(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "thickness"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "offset"),
            OptionalString(args, "shape"),
            OptionalDouble(args, "thicknessTop"),
            OptionalString(args, "position"),
            OptionalDouble(args, "horizontalOffset"),
            OptionalInt(args, "materialId"),
            OptionalString(args, "mark")),
        "renga_create_isolated_foundation" => _service.CreateIsolatedFoundation(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "width"),
            RequireDouble(args, "depth"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "elevation"),
            OptionalString(args, "shape"),
            OptionalDouble(args, "widthTop"),
            OptionalDouble(args, "depthTop"),
            OptionalDouble(args, "rotation"),
            OptionalInt(args, "materialId"),
            OptionalString(args, "mark")),
        "renga_create_room" => _service.CreateRoom(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            OptionalString(args, "name"),
            OptionalString(args, "number"),
            OptionalDouble(args, "height"),
            OptionalDouble(args, "offset")),
        "renga_create_stair" => _service.CreateStair(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "width"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "thickness"),
            OptionalInt(args, "numberOfSteps"),
            OptionalString(args, "shape"),
            OptionalString(args, "position"),
            OptionalDouble(args, "horizontalOffset"),
            OptionalDouble(args, "offset"),
            OptionalInt(args, "materialId"),
            OptionalString(args, "mark")),
        "renga_create_railing" => _service.CreateRailing(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "height"),
            OptionalDouble(args, "spaceBetweenBalusters"),
            OptionalDouble(args, "offset"),
            OptionalString(args, "mark")),
        "renga_create_ramp" => _service.CreateRamp(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "width"),
            RequireDouble(args, "height"),
            OptionalString(args, "position"),
            OptionalDouble(args, "horizontalOffset"),
            OptionalDouble(args, "offset"),
            OptionalInt(args, "materialId"),
            OptionalString(args, "mark")),
        "renga_list_objects" => _service.ListObjects(
            OptionalString(args, "type"),
            OptionalInt(args, "limit")),
        "renga_get_object" => _service.GetObject(
            RequireInt(args, "objectId"),
            OptionalBool(args, "includeProperties") ?? true),
        "renga_list_parameters" => _service.ListParameters(RequireInt(args, "objectId")),
        "renga_set_parameter" => _service.SetParameter(
            RequireInt(args, "objectId"),
            RequireString(args, "parameter"),
            RequireValue(args, "value"),
            OptionalString(args, "unit")),
        "renga_list_materials" => _service.ListMaterials(
            OptionalString(args, "kind"),
            OptionalInt(args, "limit")),
        "renga_list_styles" => _service.ListStyles(
            OptionalString(args, "kind"),
            OptionalInt(args, "limit")),
        "renga_assign_material" => _service.AssignMaterial(
            RequireInt(args, "objectId"),
            RequireInt(args, "materialId"),
            OptionalBool(args, "layered")),
        "renga_create_material" => _service.CreateMaterial(
            RequireString(args, "name"),
            OptionalDouble(args, "density")),
        "renga_create_style" => _service.CreateStyle(
            RequireString(args, "kind"),
            RequireString(args, "name"),
            OptionalInt(args, "materialId"),
            OptionalInt(args, "profileId")),
        "renga_list_style_parameters" => _service.ListStyleParameters(
            RequireString(args, "kind"),
            RequireInt(args, "styleId")),
        "renga_set_style_parameter" => _service.SetStyleParameter(
            RequireString(args, "kind"),
            RequireInt(args, "styleId"),
            RequireString(args, "parameter"),
            RequireValue(args, "value"),
            OptionalString(args, "unit")),
        "renga_list_property_definitions" => _service.ListPropertyDefinitions(),
        "renga_register_property" => _service.RegisterProperty(
            RequireString(args, "name"),
            RequireString(args, "type"),
            OptionalStringArray(args, "enumeration"),
            OptionalString(args, "propertyId")),
        "renga_assign_property" => _service.AssignProperty(
            RequireString(args, "propertyId"),
            RequireString(args, "objectType")),
        "renga_unassign_property" => _service.UnassignProperty(
            RequireString(args, "propertyId"),
            RequireString(args, "objectType")),
        "renga_unregister_property" => _service.UnregisterProperty(
            RequireString(args, "propertyId")),
        "renga_set_property" => _service.SetProperty(
            RequireInt(args, "objectId"),
            RequireString(args, "propertyId"),
            RequireValue(args, "value")),
        "renga_list_drawings" => _service.ListDrawings(),
        "renga_get_drawing_title_blocks" => _service.GetDrawingTitleBlocks(
            RequireString(args, "drawingId")),
        "renga_create_drawing" => _service.CreateDrawing(
            RequireString(args, "name"),
            OptionalString(args, "pageFormat"),
            OptionalString(args, "orientation"),
            OptionalString(args, "layoutStyle")),
        "renga_rename_drawing" => _service.RenameDrawing(
            RequireString(args, "drawingId"),
            RequireString(args, "name")),
        "renga_delete_drawing" => _service.DeleteDrawing(RequireString(args, "drawingId")),
        "renga_export_drawing" => _service.ExportDrawing(
            RequireString(args, "drawingId"),
            RequireString(args, "path"),
            OptionalString(args, "format"),
            OptionalString(args, "cadVersion")),
        "renga_export_all_drawings" => _service.ExportAllDrawings(
            RequireString(args, "directory"),
            OptionalString(args, "format"),
            OptionalString(args, "cadVersion")),
        "renga_list_drawing_resources" => _service.ListDrawingResources(),
        "renga_list_mep_categories" => _service.ListMepCategories(),
        "renga_create_route_point" => _service.CreateRoutePoint(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalString(args, "name")),
        "renga_create_pipe" => _service.CreatePipe(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "z1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "z2"),
            OptionalInt(args, "styleId"),
            OptionalInt(args, "insulationId"),
            OptionalIntArray(args, "fittingStyleIds"),
            OptionalString(args, "systemCategory"),
            OptionalString(args, "mark")),
        "renga_create_duct" => _service.CreateDuct(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "z1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "z2"),
            OptionalInt(args, "styleId"),
            OptionalInt(args, "insulationId"),
            OptionalIntArray(args, "fittingStyleIds"),
            OptionalString(args, "systemCategory"),
            OptionalString(args, "mark")),
        "renga_create_electrical_line" => _service.CreateElectricalLine(
            RequireInt(args, "levelId"),
            RequireDouble(args, "x1"),
            RequireDouble(args, "y1"),
            RequireDouble(args, "z1"),
            RequireDouble(args, "x2"),
            RequireDouble(args, "y2"),
            RequireDouble(args, "z2"),
            OptionalInt(args, "styleId"),
            OptionalString(args, "systemCategory"),
            OptionalString(args, "mark")),
        "renga_connect_mep" => _service.ConnectMep(
            RequireString(args, "kind"),
            RequireInt(args, "sourceId"),
            RequireInt(args, "sourcePort"),
            RequireInt(args, "targetId"),
            RequireInt(args, "targetPort"),
            OptionalInt(args, "styleId"),
            OptionalInt(args, "insulationId"),
            OptionalIntArray(args, "fittingStyleIds"),
            OptionalString(args, "systemCategory"),
            OptionalString(args, "mark")),
        "renga_create_mep_equipment" => _service.CreateMepEquipment(
            RequireString(args, "kind"),
            OptionalInt(args, "levelId"),
            OptionalInt(args, "hostId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalInt(args, "styleId"),
            OptionalDouble(args, "rotation"),
            OptionalString(args, "mark")),
        "renga_place_mep_component" => _service.PlaceMepComponent(
            RequireString(args, "kind"),
            RequireInt(args, "routeId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalInt(args, "styleId"),
            OptionalDouble(args, "angle"),
            OptionalString(args, "mark")),
        "renga_list_mep_routes" => _service.ListMepRoutes(OptionalInt(args, "limit")),
        "renga_create_mep_style" => _service.CreateMepStyle(
            RequireString(args, "kind"),
            RequireString(args, "name"),
            OptionalInt(args, "materialId"),
            OptionalInt(args, "categoryId"),
            OptionalDouble(args, "diameter"),
            OptionalDouble(args, "insideDiameter"),
            OptionalDouble(args, "wallThickness"),
            OptionalDouble(args, "width"),
            OptionalDouble(args, "height"),
            OptionalString(args, "profile"),
            OptionalInt(args, "conductorNumber"),
            OptionalInt(args, "conductorStyleId")),
        "renga_save_project" => _service.SaveProject(RequireString(args, "path")),
        "renga_export_ifc" => _service.ExportIfc(RequireString(args, "path")),
        "renga_delete_object" => _service.DeleteObject(
            RequireInt(args, "objectId"),
            OptionalBool(args, "confirmDependents") ?? false),
        "renga_delete_objects" => _service.DeleteObjects(
            OptionalIntArray(args, "objectIds")
                ?? throw new RengaException("Parameter 'objectIds' is required and must be an array of integers."),
            OptionalBool(args, "confirmDependents") ?? false),
        "renga_move_object" => _service.MoveObject(
            RequireInt(args, "objectId"),
            RequireDouble(args, "dx"),
            RequireDouble(args, "dy"),
            RequireDouble(args, "dz")),
        "renga_rotate_object" => _service.RotateObject(
            RequireInt(args, "objectId"),
            RequireDouble(args, "angle"),
            OptionalString(args, "axis")),
        "renga_set_object_placement" => _service.SetObjectPlacement(
            RequireInt(args, "objectId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalDouble(args, "rotation")),
        "renga_list_known_quantities" => _service.ListKnownQuantities(),
        "renga_get_quantity" => _service.GetQuantity(
            RequireInt(args, "objectId"),
            RequireString(args, "quantity"),
            OptionalString(args, "unit")),
        "renga_get_quantities" => _service.GetQuantities(
            RequireInt(args, "objectId"),
            OptionalStringArray(args, "quantities")),
        "renga_get_quantities_batch" => _service.GetQuantitiesBatch(
            OptionalIntArray(args, "objectIds")
                ?? throw new RengaException("Parameter 'objectIds' is required and must be an array of integers."),
            OptionalStringArray(args, "quantities")),
        "renga_import_ifc" => _service.ImportIfc(
            RequireString(args, "path"),
            OptionalBool(args, "confirm") ?? false),
        "renga_list_ifc_links" => _service.ListIfcLinks(OptionalInt(args, "limit")),
        "renga_list_model_links" => _service.ListModelLinks(OptionalInt(args, "limit")),
        "renga_create_ifc_link" => _service.CreateIfcLink(
            RequireString(args, "path"),
            OptionalInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalDouble(args, "rotation")),
        "renga_create_model_link" => _service.CreateModelLink(
            RequireString(args, "path"),
            OptionalInt(args, "levelId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalDouble(args, "rotation")),
        "renga_delete_link" => _service.DeleteLink(
            RequireString(args, "kind"),
            RequireInt(args, "linkId"),
            OptionalBool(args, "deleteLinkedObjects") ?? true),
        "renga_reload_link" => _service.ReloadLink(
            RequireString(args, "kind"),
            RequireInt(args, "linkId")),
        "renga_export_objects_ifc" => _service.ExportObjectsIfc(
            RequireString(args, "path"),
            OptionalIntArray(args, "objectIds")
                ?? throw new RengaException("Parameter 'objectIds' is required and must be an array of integers."),
            OptionalBool(args, "overwrite")),
        "renga_create_element_style" => _service.CreateElementStyle(
            RequireString(args, "name"),
            RequireString(args, "filePath")),
        "renga_create_element" => _service.CreateElement(
            RequireInt(args, "levelId"),
            RequireInt(args, "styleId"),
            RequireDouble(args, "x"),
            RequireDouble(args, "y"),
            RequireDouble(args, "z"),
            OptionalDouble(args, "rotation")),
        "renga_copy_object" => _service.CopyObject(
            RequireInt(args, "objectId"),
            RequireDouble(args, "dx"),
            RequireDouble(args, "dy"),
            RequireDouble(args, "dz"),
            OptionalInt(args, "targetLevelId")),
        _ => throw new RengaException($"Unknown tool: {name}")
    };

    private static bool TryGet(JsonElement args, string name, out JsonElement value)
    {
        value = default;
        return args.ValueKind == JsonValueKind.Object &&
               args.TryGetProperty(name, out value) &&
               value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
    }

    private static string RequireString(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new RengaException($"Parameter '{name}' is required and must be a string.");
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new RengaException($"Parameter '{name}' must be a string.");
        }

        return value.GetString();
    }

    private static IReadOnlyList<string>? OptionalStringArray(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new RengaException($"Parameter '{name}' must be an array of strings.");
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new RengaException($"Parameter '{name}' must be an array of strings.");
            }

            items.Add(item.GetString() ?? string.Empty);
        }

        return items;
    }

    private static IReadOnlyList<int>? OptionalIntArray(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new RengaException($"Parameter '{name}' must be an array of integers.");
        }

        var items = new List<int>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
            {
                items.Add(number);
                continue;
            }

            if (item.ValueKind == JsonValueKind.String &&
                int.TryParse(item.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                items.Add(number);
                continue;
            }

            throw new RengaException($"Parameter '{name}' must be an array of integers.");
        }

        return items;
    }

    private static bool? OptionalBool(JsonElement args, string name)    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        throw new RengaException($"Parameter '{name}' must be a boolean.");
    }

    private static JsonElement RequireValue(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            throw new RengaException($"Parameter '{name}' is required.");
        }

        return value.Clone();
    }

    private static double RequireDouble(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var result) || !double.IsFinite(result))
        {
            throw new RengaException($"Parameter '{name}' is required and must be a finite number.");
        }

        return result;
    }

    private static int RequireInt(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            throw new RengaException($"Parameter '{name}' is required and must be an integer.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new RengaException($"Parameter '{name}' is required and must be an integer.");
    }

    private static int? OptionalInt(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        throw new RengaException($"Parameter '{name}' must be an integer.");
    }

    private static double? OptionalDouble(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number) &&
            double.IsFinite(number))
        {
            return number;
        }

        throw new RengaException($"Parameter '{name}' must be a finite number.");
    }

    private static IReadOnlyList<(double X, double Y)>? OptionalPoints(JsonElement args, string name)
    {
        if (!TryGet(args, name, out var value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new RengaException($"Parameter '{name}' must be an array of points.");
        }

        var points = new List<(double X, double Y)>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("x", out var xElement) ||
                !item.TryGetProperty("y", out var yElement) ||
                xElement.ValueKind != JsonValueKind.Number ||
                yElement.ValueKind != JsonValueKind.Number ||
                !xElement.TryGetDouble(out var x) ||
                !yElement.TryGetDouble(out var y) ||
                !double.IsFinite(x) ||
                !double.IsFinite(y))
            {
                throw new RengaException($"Each point in '{name}' must have finite numeric x and y.");
            }

            points.Add((x, y));
        }

        return points;
    }

    private void SendResult(JsonElement id, object result)
    {
        Send(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.Clone(),
            ["result"] = result
        });
    }

    private void SendError(JsonElement? id, int code, string message, object? data = null)
    {
        var error = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data is not null)
        {
            error["data"] = data;
        }

        Send(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.Clone(),
            ["error"] = error
        });
    }

    private void Send(object payload)
    {
        try
        {
            _output.WriteLine(JsonSerializer.Serialize(payload, ProtocolJson));
        }
        catch (Exception ex)
        {
            Log($"Failed to write a response: {ex}");
        }
    }

    private string? ReadMessage()
    {
        var first = _input.ReadLine();
        while (first is not null && first.Length == 0)
        {
            first = _input.ReadLine();
        }

        if (first is null)
        {
            return null;
        }

        if (!first.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            return first;
        }

        if (!int.TryParse(
                first["Content-Length:".Length..].Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var length) ||
            length < 0 ||
            length > 64 * 1024 * 1024)
        {
            throw new RengaException("Invalid Content-Length header.");
        }

        var header = _input.ReadLine();
        while (!string.IsNullOrEmpty(header))
        {
            header = _input.ReadLine();
        }

        var buffer = new char[length];
        var read = 0;
        while (read < length)
        {
            var count = _input.Read(buffer, read, length - read);
            if (count <= 0)
            {
                break;
            }

            read += count;
        }

        return new string(buffer, 0, read);
    }

    private static void Log(string message) =>
        Console.Error.WriteLine($"[renga-mcp] {DateTime.Now:HH:mm:ss} {message}");
}

