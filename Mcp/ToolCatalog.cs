namespace RengaMcpServer.Mcp;

public sealed record ToolSpec(string Name, string Title, string Description, string SchemaJson);

public static class ToolCatalog
{
    public static IReadOnlyList<ToolSpec> Tools { get; } = new[]
    {
        new ToolSpec(
            "renga_ping",
            "Renga connection check",
            "Checks the connection to Renga, returns the product version, and reports the open project, levels, axes, and whether geometry exists.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_new_project",
            "Create a new Renga project",
            "Creates a new clean Renga project. Any currently open project is closed and unsaved changes are discarded.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_open_project",
            "Open a Renga project",
            "Opens an existing .rnp project. Any currently open project is closed and unsaved changes are discarded.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Path to the .rnp project file."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_levels",
            "List levels",
            "Lists the levels in the current project with their local IDs and elevations in meters. Use these IDs for walls, columns, and floors.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_level",
            "Create a level",
            "Creates a level at the given elevation. Renga's public COM API cannot set a level elevation directly, so the server uses the official IFC import route. This rebuilds the level/axis skeleton and therefore must be used before creating walls, columns, beams, or floors.",
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Level name, for example 'Level 1'."
                },
                "elevation": {
                  "type": "number",
                  "description": "Level elevation in meters."
                }
              },
              "required": ["name", "elevation"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_axis",
            "Create a coordinate axis",
            "Creates a native Renga Axis (grid line) between two points. Renga's public COM API cannot construct Axis objects directly, so the server uses the official IFC import route. Create all axes and levels before creating geometry.",
            """
            {
              "type": "object",
              "properties": {
                "name": {
                  "type": "string",
                  "description": "Axis name, for example 'A' or '1'."
                },
                "x1": {
                  "type": "number",
                  "description": "Start X in meters."
                },
                "y1": {
                  "type": "number",
                  "description": "Start Y in meters."
                },
                "x2": {
                  "type": "number",
                  "description": "End X in meters."
                },
                "y2": {
                  "type": "number",
                  "description": "End Y in meters."
                }
              },
              "required": ["name", "x1", "y1", "x2", "y2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_wall",
            "Create a wall",
            "Creates a straight wall on a level from a baseline in plan. Coordinates and height are in meters. Use renga_list_levels to obtain levelId.",
            """
            {
              "type": "object",
              "properties": {
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "height": { "type": "number", "description": "Wall height in meters." },
                "thickness": { "type": "number", "description": "Optional wall thickness in meters. Uses the Renga default when omitted." }
              },
              "required": ["x1", "y1", "x2", "y2", "levelId", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_column",
            "Create a column",
            "Creates a column at X/Y on a level. Height is in meters. If levelId is omitted, the nearest level to elevation is used; if elevation is omitted, the column starts at the level elevation.",
            """
            {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "Column X in meters." },
                "y": { "type": "number", "description": "Column Y in meters." },
                "height": { "type": "number", "description": "Column height in meters." },
                "levelId": { "type": "integer", "description": "Optional local ID of the host level." },
                "elevation": { "type": "number", "description": "Optional absolute column base elevation in meters." },
                "styleId": { "type": "integer", "description": "Optional column style ID. Use renga_list_styles with kind 'column'." }
              },
              "required": ["x", "y", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_beam",
            "Create a beam",
            "Creates a beam between two absolute 3D points. Coordinates are in meters. If levelId is omitted, the nearest level to z1 is used.",
            """
            {
              "type": "object",
              "properties": {
                "x1": { "type": "number", "description": "Start X in meters." },
                "y1": { "type": "number", "description": "Start Y in meters." },
                "z1": { "type": "number", "description": "Start Z in meters." },
                "x2": { "type": "number", "description": "End X in meters." },
                "y2": { "type": "number", "description": "End Y in meters." },
                "z2": { "type": "number", "description": "End Z in meters." },
                "levelId": { "type": "integer", "description": "Optional local ID of the host level." },
                "styleId": { "type": "integer", "description": "Optional beam style ID. Use renga_list_styles with kind 'beam'." }
              },
              "required": ["x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_door",
            "Create a door",
            "Creates a door hosted on a wall. Coordinates, width, and height are in meters. The wall must already exist.",
            """
            {
              "type": "object",
              "properties": {
                "wallId": { "type": "integer", "description": "Local ID of the host wall." },
                "x": { "type": "number", "description": "Door center X on the wall, in meters." },
                "y": { "type": "number", "description": "Door center Y on the wall, in meters." },
                "width": { "type": "number", "description": "Door width in meters." },
                "height": { "type": "number", "description": "Door height in meters." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "styleId": { "type": "integer", "description": "Optional door style ID. Use renga_list_styles with kind 'door'. Default 0 means no style." }
              },
              "required": ["wallId", "x", "y", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_window",
            "Create a window",
            "Creates a window hosted on a wall. Coordinates, width, height, and sill are in meters. The wall must already exist.",
            """
            {
              "type": "object",
              "properties": {
                "wallId": { "type": "integer", "description": "Local ID of the host wall." },
                "x": { "type": "number", "description": "Window center X on the wall, in meters." },
                "y": { "type": "number", "description": "Window center Y on the wall, in meters." },
                "width": { "type": "number", "description": "Window width in meters." },
                "height": { "type": "number", "description": "Window height in meters." },
                "sill": { "type": "number", "description": "Optional sill height from the level in meters. Default 0.9." },
                "styleId": { "type": "integer", "description": "Optional window style ID. Use renga_list_styles with kind 'window'. Default 0 means no style." }
              },
              "required": ["wallId", "x", "y", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_roof",
            "Create a flat roof",
            "Creates a flat roof slab on a level from a closed polygon or a rectangle. Points are in meters, thickness and offset are in meters. Provide either points or x1, y1, x2, y2.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "thickness": { "type": "number", "description": "Roof thickness in meters." },
                "offset": { "type": "number", "description": "Optional roof base elevation above the level in meters. Default 0." },
                "points": {
                  "type": "array",
                  "description": "Closed or open polygon of at least 3 points in meters.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x": { "type": "number" },
                      "y": { "type": "number" }
                    },
                    "required": ["x", "y"],
                    "additionalProperties": false
                  }
                },
                "x1": { "type": "number", "description": "Rectangle corner 1 X in meters." },
                "y1": { "type": "number", "description": "Rectangle corner 1 Y in meters." },
                "x2": { "type": "number", "description": "Rectangle corner 2 X in meters." },
                "y2": { "type": "number", "description": "Rectangle corner 2 Y in meters." }
              },
              "required": ["levelId", "thickness"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_floor",
            "Create a floor slab",
            "Creates a floor (перекрытие) on a level from a closed polygon or a rectangle. Points and thickness are in meters. Provide either points or x1, y1, x2, y2.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "thickness": { "type": "number", "description": "Floor thickness in meters." },
                "points": {
                  "type": "array",
                  "description": "Closed or open polygon of at least 3 points in meters.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x": { "type": "number" },
                      "y": { "type": "number" }
                    },
                    "required": ["x", "y"],
                    "additionalProperties": false
                  }
                },
                "x1": { "type": "number", "description": "Rectangle corner 1 X in meters." },
                "y1": { "type": "number", "description": "Rectangle corner 1 Y in meters." },
                "x2": { "type": "number", "description": "Rectangle corner 2 X in meters." },
                "y2": { "type": "number", "description": "Rectangle corner 2 Y in meters." }
              },
              "required": ["levelId", "thickness"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_opening",
            "Create an opening",
            "Creates an opening (проём) in a floor or roof slab from a closed polygon or rectangle. Openings are hosted by floors and roofs; wall apertures use doors or windows. Points and depth are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "hostId": { "type": "integer", "description": "Local ID of the host floor or roof." },
                "points": {
                  "type": "array",
                  "description": "Closed or open polygon of at least 3 points in meters.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x": { "type": "number" },
                      "y": { "type": "number" }
                    },
                    "required": ["x", "y"],
                    "additionalProperties": false
                  }
                },
                "x1": { "type": "number", "description": "Rectangle corner 1 X in meters." },
                "y1": { "type": "number", "description": "Rectangle corner 1 Y in meters." },
                "x2": { "type": "number", "description": "Rectangle corner 2 X in meters." },
                "y2": { "type": "number", "description": "Rectangle corner 2 Y in meters." },
                "depth": { "type": "number", "description": "Optional opening depth in meters. Uses the host thickness when omitted." },
                "offset": { "type": "number", "description": "Optional vertical offset from the host in meters. Default 0." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["hostId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_plate",
            "Create a plate",
            "Creates a native Renga Plate (пластина) at X/Y on a level. This is a profile-based plate, not a floor slab. Thickness and elevation are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x": { "type": "number", "description": "Insert X in meters." },
                "y": { "type": "number", "description": "Insert Y in meters." },
                "thickness": { "type": "number", "description": "Optional plate thickness in meters." },
                "elevation": { "type": "number", "description": "Optional absolute base elevation in meters." },
                "styleId": { "type": "integer", "description": "Optional plate style ID. Use renga_list_styles with kind 'plate'." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x", "y"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_wall_foundation",
            "Create a wall foundation",
            "Creates a strip (wall) foundation along a straight baseline on a level. Coordinates, thickness, and height are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "thickness": { "type": "number", "description": "Foundation thickness in meters." },
                "height": { "type": "number", "description": "Foundation height in meters." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "shape": { "type": "string", "description": "Optional shape: rectangular or trapezoid. Default rectangular." },
                "thicknessTop": { "type": "number", "description": "Optional top thickness in meters for trapezoid shape." },
                "position": { "type": "string", "description": "Optional position relative to the baseline: right, center, or left. Default center." },
                "horizontalOffset": { "type": "number", "description": "Optional horizontal offset in meters. Default 0." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "x2", "y2", "thickness", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_isolated_foundation",
            "Create an isolated foundation",
            "Creates an isolated (column) foundation at X/Y on a level. Dimensions are in meters. Shape can be rectangular or trapezoid.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x": { "type": "number", "description": "Foundation center X in meters." },
                "y": { "type": "number", "description": "Foundation center Y in meters." },
                "width": { "type": "number", "description": "Bottom width in meters." },
                "depth": { "type": "number", "description": "Bottom depth in meters." },
                "height": { "type": "number", "description": "Foundation height in meters." },
                "elevation": { "type": "number", "description": "Optional absolute base elevation in meters." },
                "shape": { "type": "string", "description": "Optional shape: rectangular or trapezoid. Default rectangular." },
                "widthTop": { "type": "number", "description": "Optional top width in meters for trapezoid shape." },
                "depthTop": { "type": "number", "description": "Optional top depth in meters for trapezoid shape." },
                "rotation": { "type": "number", "description": "Optional rotation angle in degrees." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x", "y", "width", "depth", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_room",
            "Create a room",
            "Creates a native room at a control point on a level. The room region is computed from the surrounding walls. Coordinates, height, and offset are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x": { "type": "number", "description": "Control point X in meters." },
                "y": { "type": "number", "description": "Control point Y in meters." },
                "name": { "type": "string", "description": "Optional room name." },
                "number": { "type": "string", "description": "Optional room number." },
                "height": { "type": "number", "description": "Optional room height in meters." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." }
              },
              "required": ["levelId", "x", "y"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_stair",
            "Create a stair",
            "Creates a stair along a straight baseline on a level. Coordinates, width, height, and thickness are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "width": { "type": "number", "description": "Stair width in meters." },
                "height": { "type": "number", "description": "Stair height in meters." },
                "thickness": { "type": "number", "description": "Optional stair thickness in meters." },
                "numberOfSteps": { "type": "integer", "description": "Optional number of steps." },
                "shape": { "type": "string", "description": "Optional shape: contour, thickened, solid, or open." },
                "position": { "type": "string", "description": "Optional position relative to the baseline: right, center, or left. Default center." },
                "horizontalOffset": { "type": "number", "description": "Optional horizontal offset in meters. Default 0." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "x2", "y2", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_railing",
            "Create a railing",
            "Creates a railing along a straight baseline on a level. Coordinates, height, and spacing are in meters. Renga keeps a straight baseline for railings.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "height": { "type": "number", "description": "Railing height in meters." },
                "spaceBetweenBalusters": { "type": "number", "description": "Optional space between balusters in meters." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "x2", "y2", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_ramp",
            "Create a ramp",
            "Creates a ramp along a straight baseline on a level. Coordinates, width, and height are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Baseline start X in meters." },
                "y1": { "type": "number", "description": "Baseline start Y in meters." },
                "x2": { "type": "number", "description": "Baseline end X in meters." },
                "y2": { "type": "number", "description": "Baseline end Y in meters." },
                "width": { "type": "number", "description": "Ramp width in meters." },
                "height": { "type": "number", "description": "Ramp height in meters." },
                "position": { "type": "string", "description": "Optional position relative to the baseline: right, center, or left. Default center." },
                "horizontalOffset": { "type": "number", "description": "Optional horizontal offset in meters. Default 0." },
                "offset": { "type": "number", "description": "Optional vertical offset from the level in meters. Default 0." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "x2", "y2", "width", "height"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_objects",
            "List model objects",
            "Lists model objects with local IDs, unique IDs, types, names, and host level IDs. Optionally filters by type name or type GUID.",
            """
            {
              "type": "object",
              "properties": {
                "type": { "type": "string", "description": "Optional type name (wall, door, window, level, ...) or type GUID." },
                "limit": { "type": "integer", "description": "Optional maximum number of objects. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_object",
            "Get object details",
            "Returns object identity, host information, parameters, custom properties, and the 3D placement of level objects.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "includeProperties": { "type": "boolean", "description": "Include custom properties. Default true." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_parameters",
            "List object parameters",
            "Lists all native parameters of an object with GUIDs, names, types, read-only state, raw values, and human-readable values.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_parameter",
            "Set an object parameter",
            "Sets a native parameter by GUID or name. Length and elevation values default to meters, angles to degrees, area to m2, volume to m3, mass to kg. Use unit to override.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "parameter": { "type": "string", "description": "Parameter GUID or parameter name." },
                "value": { "type": ["number", "string", "boolean"], "description": "New parameter value." },
                "unit": { "type": "string", "description": "Optional unit: m, mm, cm, deg, rad, m2, mm2, cm2, m3, mm3, cm3, kg, g, t, or raw." }
              },
              "required": ["objectId", "parameter", "value"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_materials",
            "List materials",
            "Lists simple materials and layered materials available in the project.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Optional filter: all, material, or layered." },
                "limit": { "type": "integer", "description": "Optional maximum number of items per collection. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_styles",
            "List styles",
            "Lists style collections and their items. Without kind it returns collection names and counts; with kind it returns style IDs and names. System styles include category, designation, and color; profiles include descriptionId and descriptionName; beam and column styles include profile information when available.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Optional style kind: beam, column, door, window, plate, element, profile, pipe, duct, material, layered_material, and others." },
                "limit": { "type": "integer", "description": "Optional maximum number of items. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_assign_material",
            "Assign a material",
            "Assigns a simple material or layered material to an object. Columns and beams typically use MaterialId; walls, floors, and roofs use LayeredMaterialId.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "materialId": { "type": "integer", "description": "Material or layered material local ID." },
                "layered": { "type": "boolean", "description": "Optional explicit flag: true for layered material, false for simple material." }
              },
              "required": ["objectId", "materialId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_material",
            "Create a material",
            "Creates a simple material in the project. Walls, floors, and roofs require layered materials for material assignment.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Material name." },
                "density": { "type": "number", "description": "Optional mass density in kg/m3." }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_style",
            "Create a style",
            "Creates a native style. Supported kinds: door, window, beam, column, plate. Beam, column, and plate styles may require an existing profileId.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind: door, window, beam, column, or plate." },
                "name": { "type": "string", "description": "Style name." },
                "materialId": { "type": "integer", "description": "Optional material ID for the style." },
                "profileId": { "type": "integer", "description": "Optional profile ID for beam, column, or plate styles." }
              },
              "required": ["kind", "name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_style_parameters",
            "List style parameters",
            "Lists parameters of a style entity. Use renga_list_styles to find style IDs.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind, for example door, window, beam, column, plate, material, or layered_material." },
                "styleId": { "type": "integer", "description": "Local style entity ID." }
              },
              "required": ["kind", "styleId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_style_parameter",
            "Set a style parameter",
            "Sets a parameter of a style entity by GUID or name. The same unit rules as renga_set_parameter apply.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Style kind." },
                "styleId": { "type": "integer", "description": "Local style entity ID." },
                "parameter": { "type": "string", "description": "Parameter GUID or name." },
                "value": { "type": ["number", "string", "boolean"], "description": "New value." },
                "unit": { "type": "string", "description": "Optional unit: m, mm, cm, deg, rad, m2, m3, kg, raw, etc." }
              },
              "required": ["kind", "styleId", "parameter", "value"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_property_definitions",
            "List custom property definitions",
            "Lists registered custom properties in the project with IDs, names, types, and enumeration values.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_register_property",
            "Register a custom property",
            "Registers a new custom property definition for the project. The property must then be assigned to an object type.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Property name." },
                "type": { "type": "string", "description": "Property type: string, double, length, angle, area, boolean, enumeration, integer, logical, mass, volume." },
                "enumeration": {
                  "type": "array",
                  "description": "Optional enumeration values for type 'enumeration'.",
                  "items": { "type": "string" }
                },
                "propertyId": { "type": "string", "description": "Optional GUID. A new GUID is generated when omitted." }
              },
              "required": ["name", "type"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_assign_property",
            "Assign a custom property to a type",
            "Assigns a registered custom property to an object type, for example wall, door, or window.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "objectType": { "type": "string", "description": "Object type name or type GUID." }
              },
              "required": ["propertyId", "objectType"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_unassign_property",
            "Unassign a custom property from a type",
            "Removes a custom property assignment from an object type.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "objectType": { "type": "string", "description": "Object type name or type GUID." }
              },
              "required": ["propertyId", "objectType"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_unregister_property",
            "Unregister a custom property",
            "Removes a custom property definition from the project.",
            """
            {
              "type": "object",
              "properties": {
                "propertyId": { "type": "string", "description": "Custom property GUID." }
              },
              "required": ["propertyId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_property",
            "Set a custom property value",
            "Sets a custom property value on an object. The property must first be registered and assigned to the object type.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "propertyId": { "type": "string", "description": "Custom property GUID." },
                "value": { "type": ["number", "string", "boolean"], "description": "New value." }
              },
              "required": ["objectId", "propertyId", "value"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_drawings",
            "List drawings",
            "Lists sheets (drawings) in the project with their index, entity ID, name, page format, layout style, and title block count.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_drawing_title_blocks",
            "Read drawing title blocks",
            "Reads title block instances of a drawing, including row and column values. Title blocks are read-only.",
            """
            {
              "type": "object",
              "properties": {
                "drawingId": { "type": "string", "description": "Drawing GUID from renga_list_drawings." }
              },
              "required": ["drawingId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_drawing",
            "Create a drawing sheet",
            "Creates a new drawing (sheet). Page format and layout style can be specified by name or ID. Orientation is portrait or landscape.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Drawing name." },
                "pageFormat": { "type": "string", "description": "Optional page format name (for example A4) or ID. Use renga_list_drawing_resources." },
                "layoutStyle": { "type": "string", "description": "Optional layout style name or ID. Use renga_list_drawing_resources." },
                "orientation": { "type": "string", "description": "Optional orientation: portrait or landscape." }
              },
              "required": ["name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_rename_drawing",
            "Rename a drawing",
            "Renames a drawing sheet by its drawingId. Use renga_list_drawings to obtain drawingId.",
            """
            {
              "type": "object",
              "properties": {
                "drawingId": { "type": "string", "description": "Drawing GUID from renga_list_drawings." },
                "name": { "type": "string", "description": "New drawing name." }
              },
              "required": ["drawingId", "name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_delete_drawing",
            "Delete a drawing",
            "Deletes a drawing sheet by its drawingId. Use renga_list_drawings to obtain drawingId.",
            """
            {
              "type": "object",
              "properties": {
                "drawingId": { "type": "string", "description": "Drawing GUID from renga_list_drawings." }
              },
              "required": ["drawingId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_export_drawing",
            "Export a drawing",
            "Exports a drawing sheet to PDF, DXF, DWG, or XPS. The matching extension is appended to the path when missing.",
            """
            {
              "type": "object",
              "properties": {
                "drawingId": { "type": "string", "description": "Drawing GUID from renga_list_drawings." },
                "path": { "type": "string", "description": "Destination file path." },
                "format": { "type": "string", "description": "Optional format: pdf, dxf, dwg, or xps. Default pdf." },
                "cadVersion": { "type": "string", "description": "Optional AutoCAD version for DXF/DWG: v2000, v2004, v2007, v2010, v2013, or v2018. Default v2018." }
              },
              "required": ["drawingId", "path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_export_all_drawings",
            "Export all drawings",
            "Exports every drawing sheet to a directory as PDF, DXF, DWG, or XPS files.",
            """
            {
              "type": "object",
              "properties": {
                "directory": { "type": "string", "description": "Destination directory." },
                "format": { "type": "string", "description": "Optional format: pdf, dxf, dwg, or xps. Default pdf." },
                "cadVersion": { "type": "string", "description": "Optional AutoCAD version for DXF/DWG. Default v2018." }
              },
              "required": ["directory"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_drawing_resources",
            "List drawing resources",
            "Lists page formats, layout styles, scales, and legend styles available for drawings.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_mep_categories",
            "List MEP categories",
            "Lists pipe, duct, and electrical system categories and the style categories used by MEP equipment, fittings, and accessories.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_route_point",
            "Create a route point",
            "Creates a route point (точка трассировки) on a level. Route points are anchors for pipe, duct, and electrical routes. Coordinates are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x": { "type": "number", "description": "Point X in meters." },
                "y": { "type": "number", "description": "Point Y in meters." },
                "z": { "type": "number", "description": "Absolute point Z in meters." },
                "name": { "type": "string", "description": "Optional route point name." }
              },
              "required": ["levelId", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_pipe",
            "Create a pipe route",
            "Creates a pipe route between two 3D points. The server creates two route points and connects them through Renga's engineering object connector. Coordinates are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Start X in meters." },
                "y1": { "type": "number", "description": "Start Y in meters." },
                "z1": { "type": "number", "description": "Start Z in meters." },
                "x2": { "type": "number", "description": "End X in meters." },
                "y2": { "type": "number", "description": "End Y in meters." },
                "z2": { "type": "number", "description": "End Z in meters." },
                "styleId": { "type": "integer", "description": "Optional pipe style ID. Uses the first pipe style when omitted." },
                "insulationId": { "type": "integer", "description": "Optional insulation layered material ID. Default 0 (none)." },
                "fittingStyleIds": {
                  "type": "array",
                  "description": "Optional compatible pipe fitting style IDs.",
                  "items": { "type": "integer" }
                },
                "systemCategory": { "type": "string", "description": "Optional system category name or value. Default domestic_cold_water." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_duct",
            "Create a duct route",
            "Creates a duct route between two 3D points. The server creates two route points and connects them through Renga's engineering object connector. Coordinates are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Start X in meters." },
                "y1": { "type": "number", "description": "Start Y in meters." },
                "z1": { "type": "number", "description": "Start Z in meters." },
                "x2": { "type": "number", "description": "End X in meters." },
                "y2": { "type": "number", "description": "End Y in meters." },
                "z2": { "type": "number", "description": "End Z in meters." },
                "styleId": { "type": "integer", "description": "Optional duct style ID. Uses the first duct style when omitted." },
                "insulationId": { "type": "integer", "description": "Optional insulation layered material ID. Default 0 (none)." },
                "fittingStyleIds": {
                  "type": "array",
                  "description": "Optional compatible duct fitting style IDs.",
                  "items": { "type": "integer" }
                },
                "systemCategory": { "type": "string", "description": "Optional system category name or value. Default ventilation." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_electrical_line",
            "Create an electrical circuit line",
            "Creates an electrical circuit line between two 3D points. The server creates two route points and connects them through Renga's engineering object connector. Coordinates are in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Local ID of the host level." },
                "x1": { "type": "number", "description": "Start X in meters." },
                "y1": { "type": "number", "description": "Start Y in meters." },
                "z1": { "type": "number", "description": "Start Z in meters." },
                "x2": { "type": "number", "description": "End X in meters." },
                "y2": { "type": "number", "description": "End Y in meters." },
                "z2": { "type": "number", "description": "End Z in meters." },
                "styleId": { "type": "integer", "description": "Optional electrical circuit line style ID. Uses the first style when omitted." },
                "systemCategory": { "type": "string", "description": "Optional system category name or value. Default lighting_circuit." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["levelId", "x1", "y1", "z1", "x2", "y2", "z2"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_connect_mep",
            "Connect MEP objects",
            "Connects two MEP objects with ports (equipment, route points, or a route) using Renga's engineering object connector. Use port index -1 for a route endpoint.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "Connection kind: pipe, duct, or electrical." },
                "sourceId": { "type": "integer", "description": "Source object ID." },
                "sourcePort": { "type": "integer", "description": "Zero-based source port index." },
                "targetId": { "type": "integer", "description": "Target object ID." },
                "targetPort": { "type": "integer", "description": "Zero-based target port index, or -1 for a route endpoint." },
                "styleId": { "type": "integer", "description": "Optional pipe, duct, or electrical line style ID." },
                "insulationId": { "type": "integer", "description": "Optional insulation layered material ID. Default 0." },
                "fittingStyleIds": {
                  "type": "array",
                  "description": "Optional fitting style IDs for pipe or duct connections.",
                  "items": { "type": "integer" }
                },
                "systemCategory": { "type": "string", "description": "Optional system category name or value." },
                "mark": { "type": "string", "description": "Optional mark for the created MEP object." }
              },
              "required": ["kind", "sourceId", "sourcePort", "targetId", "targetPort"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_mep_equipment",
            "Create MEP equipment",
            "Creates MEP equipment or a fixture. Equipment, mechanical equipment, and plumbing fixtures are hosted by a level; lighting fixtures, wiring accessories, and distribution boards require hostId (wall, column, beam, or floor). StyleId is required by Renga for these objects.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "equipment, mechanical_equipment, plumbing_fixture, lighting_fixture, wiring_accessory, or electric_distribution_board." },
                "levelId": { "type": "integer", "description": "Local ID of the host level. Required for level-hosted kinds." },
                "hostId": { "type": "integer", "description": "Host object ID (wall, column, beam, or floor) for hosted fixtures." },
                "x": { "type": "number", "description": "X in meters." },
                "y": { "type": "number", "description": "Y in meters." },
                "z": { "type": "number", "description": "Z in meters. Absolute for hosted fixtures, absolute elevation for level-hosted objects." },
                "styleId": { "type": "integer", "description": "Optional style ID. Uses the first style of the kind when omitted." },
                "rotation": { "type": "number", "description": "Optional rotation angle in degrees." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["kind", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_place_mep_component",
            "Place a fitting or accessory",
            "Places a pipe/duct fitting or accessory on an existing route. Renga validates style compatibility; for example, elbows and tees may fail on a straight route.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "pipe_fitting, pipe_accessory, duct_fitting, or duct_accessory." },
                "routeId": { "type": "integer", "description": "Local ID of the host route." },
                "x": { "type": "number", "description": "Projection point X in meters." },
                "y": { "type": "number", "description": "Projection point Y in meters." },
                "z": { "type": "number", "description": "Projection point Z in meters." },
                "styleId": { "type": "integer", "description": "Optional component style ID. Uses the first style of the kind when omitted." },
                "angle": { "type": "number", "description": "Optional rotation angle in degrees." },
                "mark": { "type": "string", "description": "Optional mark." }
              },
              "required": ["kind", "routeId", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_mep_routes",
            "List MEP routes",
            "Lists routes with source and target ports, system style, contour length, segments, joints, and objects placed on the route.",
            """
            {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "description": "Optional maximum number of routes. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_mep_style",
            "Create an MEP style",
            "Creates an MEP style. Pipe, duct, and electrical line styles support dimensions; fitting and accessory styles require categoryId from renga_list_mep_categories.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "description": "pipe, duct, electrical_circuit_line, pipe_fitting, pipe_accessory, duct_fitting, or duct_accessory." },
                "name": { "type": "string", "description": "Style name." },
                "materialId": { "type": "integer", "description": "Optional material ID." },
                "categoryId": { "type": "integer", "description": "Category ID for fitting and accessory styles." },
                "diameter": { "type": "number", "description": "Optional pipe outside diameter in meters." },
                "insideDiameter": { "type": "number", "description": "Optional pipe inside diameter in meters." },
                "wallThickness": { "type": "number", "description": "Optional pipe or duct wall thickness in meters." },
                "width": { "type": "number", "description": "Optional rectangular duct width in meters." },
                "height": { "type": "number", "description": "Optional rectangular duct height in meters." },
                "profile": { "type": "string", "description": "Optional duct profile: circle or rectangle." },
                "conductorNumber": { "type": "integer", "description": "Optional number of conductors for an electrical circuit line style." },
                "conductorStyleId": { "type": "integer", "description": "Optional conductor style ID for an electrical circuit line style." }
              },
              "required": ["kind", "name"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_save_project",
            "Save the Renga project",
            "Saves the current project to the specified .rnp path. The .rnp extension is appended if missing.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Destination .rnp file path."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_export_ifc",
            "Export the model to IFC",
            "Exports the current Renga model to an IFC file. The .ifc extension is appended if missing.",
            """
            {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Destination .ifc file path."
                }
              },
              "required": ["path"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_delete_object",
            "Delete a model object",
            "Deletes a model object. When a host is deleted, Renga also deletes its dependent objects (door, window, opening, objects on a route, and so on). If dependents exist, the call fails unless confirmDependents is true. Returns requested, dependent, and actually deleted IDs plus the remaining object count.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "confirmDependents": { "type": "boolean", "description": "Allow deleting dependent objects together with the host. Default false." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_delete_objects",
            "Delete several model objects",
            "Deletes several model objects in one operation. Dependent objects are deleted with their hosts. If any requested object has dependents, the call fails unless confirmDependents is true. Returns dependent and actually deleted IDs plus the remaining object count.",
            """
            {
              "type": "object",
              "properties": {
                "objectIds": { "type": "array", "items": { "type": "integer" }, "description": "Local model object IDs." },
                "confirmDependents": { "type": "boolean", "description": "Allow deleting dependent objects together with their hosts. Default false." }
              },
              "required": ["objectIds"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_move_object",
            "Move a model object",
            "Moves a level object by dx, dy, and dz in meters using ILevelObject placement. Doors, windows, openings, rooms, MEP lines/routes, and hosts with dependent objects cannot be moved. The server verifies that Renga actually applied the new placement and reports the result.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "dx": { "type": "number", "description": "Offset along global X in meters." },
                "dy": { "type": "number", "description": "Offset along global Y in meters." },
                "dz": { "type": "number", "description": "Vertical offset in meters, relative to the level." }
              },
              "required": ["objectId", "dx", "dy", "dz"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_rotate_object",
            "Rotate a model object",
            "Rotates a level object around its own origin about the global X, Y, or Z axis. The angle is in degrees. Doors, windows, openings, rooms, MEP lines/routes, and hosts with dependent objects cannot be rotated. The server verifies the result.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "angle": { "type": "number", "description": "Rotation angle in degrees." },
                "axis": { "type": "string", "enum": ["x", "y", "z"], "description": "Global rotation axis. Default z." }
              },
              "required": ["objectId", "angle"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_set_object_placement",
            "Set absolute object placement",
            "Sets the absolute placement origin of a level object: x and y in project coordinates, z as an absolute elevation in meters. An optional rotation sets the object orientation around Z. The same placement restrictions apply as for move and rotate. The server verifies the result.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "x": { "type": "number", "description": "Absolute placement origin X in meters." },
                "y": { "type": "number", "description": "Absolute placement origin Y in meters." },
                "z": { "type": "number", "description": "Absolute placement elevation in meters." },
                "rotation": { "type": "number", "description": "Optional rotation around Z in degrees. When omitted, the current orientation is kept." }
              },
              "required": ["objectId", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_known_quantities",
            "List known quantity IDs",
            "Lists the known Renga quantity GUIDs with names, quantity types, and default units. Renga does not expose a way to enumerate quantity IDs, so this static catalog is used by renga_get_quantity and renga_get_quantities.",
            """
            {
              "type": "object",
              "properties": {},
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_quantity",
            "Get one object quantity",
            "Reads a quantity from an object by name or GUID. Use renga_list_known_quantities for the catalog. Units: m, mm, cm for lengths and elevations; m2, mm2, cm2 for areas; m3, mm3, cm3 for volumes; kg, g, t for mass; deg or rad for angles.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "quantity": { "type": "string", "description": "Quantity name (NetVolume, GrossArea, ...) or GUID." },
                "unit": { "type": "string", "description": "Optional output unit. Defaults to m, m2, m3, kg, deg, or count." }
              },
              "required": ["objectId", "quantity"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_quantities",
            "Get object quantities",
            "Reads several quantities from an object. Without a list, returns all supported quantities from the known catalog. Explicit names that are not supported are returned with supported=false; unknown names cause an error.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Local model object ID." },
                "quantities": { "type": "array", "items": { "type": "string" }, "description": "Optional quantity names or GUIDs." }
              },
              "required": ["objectId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_get_quantities_batch",
            "Get quantities for several objects",
            "Reads quantities for several objects in one call. Without a quantity list, returns all supported catalog quantities for each object. Missing object IDs are reported. At most 500 objects are processed.",
            """
            {
              "type": "object",
              "properties": {
                "objectIds": { "type": "array", "items": { "type": "integer" }, "description": "Local model object IDs." },
                "quantities": { "type": "array", "items": { "type": "string" }, "description": "Optional quantity names or GUIDs." }
              },
              "required": ["objectIds"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_import_ifc",
            "Import an IFC file as a new project",
            "Imports an IFC file through IApplication.ImportIfcProject. The current project is closed without saving and replaced by the imported model. confirm must be true. Use renga_save_project before importing.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute path to a .ifc file." },
                "confirm": { "type": "boolean", "description": "Must be true: the current project is discarded." }
              },
              "required": ["path", "confirm"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_ifc_links",
            "List IFC links",
            "Lists IFC links and the model objects created from them. Returns link IDs, names, saved and resolved absolute paths, file existence, link file type, and linked object IDs with placements. Also reports IfcModel objects that have no registered link.",
            """
            {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "description": "Optional maximum number of links. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_list_model_links",
            "List 3D model links",
            "Lists linked 3D model files and the Model3D objects created from them. Returns link IDs, names, saved and resolved absolute paths, file existence, and linked object IDs with placements.",
            """
            {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "description": "Optional maximum number of links. Default 500." }
              },
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_ifc_link",
            "Link an IFC file into the model",
            "Creates an IfcModel object linked to an IFC file in the current project. levelId selects the host level; when omitted, the level is chosen by z. z is an absolute elevation in meters. Renga reports the link file type and may refuse unsupported IFC versions.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute path to a .ifc file." },
                "levelId": { "type": "integer", "description": "Optional host level ID. When omitted, the nearest level to z is used." },
                "x": { "type": "number", "description": "Insertion X in meters." },
                "y": { "type": "number", "description": "Insertion Y in meters." },
                "z": { "type": "number", "description": "Absolute insertion elevation in meters." },
                "rotation": { "type": "number", "description": "Optional rotation around Z in degrees." }
              },
              "required": ["path", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_model_link",
            "Link a 3D model file into the model",
            "Creates a Model3D object linked to an external 3D file. levelId selects the host level; when omitted, the level is chosen by z. z is an absolute elevation in meters. Supported 3D formats depend on the Renga license.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Absolute path to a supported 3D file." },
                "levelId": { "type": "integer", "description": "Optional host level ID. When omitted, the nearest level to z is used." },
                "x": { "type": "number", "description": "Insertion X in meters." },
                "y": { "type": "number", "description": "Insertion Y in meters." },
                "z": { "type": "number", "description": "Absolute insertion elevation in meters." },
                "rotation": { "type": "number", "description": "Optional rotation around Z in degrees." }
              },
              "required": ["path", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_delete_link",
            "Delete an IFC or model link",
            "Deletes a link and optionally its linked model objects. The link is removed from IProject.IfcLinks or IProject.ModelLinks in the same operation. By default linked objects are deleted too.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "enum": ["ifc", "model"], "description": "Link kind: ifc or model." },
                "linkId": { "type": "integer", "description": "Link ID from renga_list_ifc_links or renga_list_model_links." },
                "deleteLinkedObjects": { "type": "boolean", "description": "Delete linked objects together with the link. Default true." }
              },
              "required": ["kind", "linkId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_reload_link",
            "Reload an IFC or model link",
            "Recreates a link from its saved file at the same placement. Only links with exactly one linked object are supported. The old link and object are removed and recreated, so local IDs change. The file must still exist.",
            """
            {
              "type": "object",
              "properties": {
                "kind": { "type": "string", "enum": ["ifc", "model"], "description": "Link kind: ifc or model." },
                "linkId": { "type": "integer", "description": "Link ID from renga_list_ifc_links or renga_list_model_links." }
              },
              "required": ["kind", "linkId"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_export_objects_ifc",
            "Export selected objects to IFC",
            "Exports only the specified objects to IFC using IProject.ExportObjectsToIFC. Dependent objects and relationships are exported by Renga according to its own rules.",
            """
            {
              "type": "object",
              "properties": {
                "path": { "type": "string", "description": "Destination .ifc file path." },
                "objectIds": { "type": "array", "items": { "type": "integer" }, "description": "Local model object IDs to export." },
                "overwrite": { "type": "boolean", "description": "Overwrite an existing file. Default true." }
              },
              "required": ["path", "objectIds"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_element_style",
            "Create an element style from a 3D file",
            "Creates an ElementStyle from a 3D file through INewEntityArgs.FilePath. Supported formats depend on the Renga license (for example .obj or .ifc). The file geometry defines the element; Renga returns an error when the format is not supported.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Style name." },
                "filePath": { "type": "string", "description": "Absolute path to a supported 3D file." }
              },
              "required": ["name", "filePath"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_create_element",
            "Create an element from a style",
            "Creates a native Element on a level using an ElementStyle created from a 3D file. x and y are in meters; z is an absolute elevation in meters.",
            """
            {
              "type": "object",
              "properties": {
                "levelId": { "type": "integer", "description": "Host level ID." },
                "styleId": { "type": "integer", "description": "ElementStyle ID from renga_create_element_style or renga_list_styles kind=element." },
                "x": { "type": "number", "description": "Insertion X in meters." },
                "y": { "type": "number", "description": "Insertion Y in meters." },
                "z": { "type": "number", "description": "Absolute insertion elevation in meters." },
                "rotation": { "type": "number", "description": "Optional rotation around Z in degrees." }
              },
              "required": ["levelId", "styleId", "x", "y", "z"],
              "additionalProperties": false
            }
            """),
        new ToolSpec(
            "renga_copy_object",
            "Copy a simple model object",
            "Recreates a wall, column, beam, floor, roof, plate, stair, ramp, railing, wall foundation, or isolated foundation with an offset, copying placement, baseline, style, material, and core dimensions. targetLevelId lets the copy live on another level. Doors and windows of a copied wall are recreated; other dependent objects are not copied. Renga has no universal clone API, so rooms, MEP objects, and unsupported types return an error.",
            """
            {
              "type": "object",
              "properties": {
                "objectId": { "type": "integer", "description": "Source object ID." },
                "dx": { "type": "number", "description": "Offset along global X in meters." },
                "dy": { "type": "number", "description": "Offset along global Y in meters." },
                "dz": { "type": "number", "description": "Vertical offset in meters." },
                "targetLevelId": { "type": "integer", "description": "Optional target level ID. Defaults to the source level." }
              },
              "required": ["objectId", "dx", "dy", "dz"],
              "additionalProperties": false
            }
            """)
    };
}
