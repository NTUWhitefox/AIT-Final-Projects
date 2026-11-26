# Low Poly Japanese Town Buildings – Apartments, Danchi, Shops, Offices and Stackable Background Building Modules — FBX

Thanks for getting this pack! If it helps your project, a quick rating on Fab really helps a small solo shop.

15 stylized low-poly Japanese town buildings, 6 street / rooftop props and 14 stackable building modules (+ 22 buildings in 2 extra colour schemes) to fill the streets and the background of a Japanese town. Formats: GLB / FBX / Blend (one zip per format, see Downloads below). Godot 4 sample project included (GLB zip); usable in Unity / Unreal.
Apartment buildings (5F and 10F mansions, a 24F tower, two danchi housing blocks, a 2F wooden apartment), commercial buildings (pencil building with vertical sign, tenant building, office building, shop house, kanban-style shop, warehouse / workshop, parking garage), two houses, and props: danchi water tower, rooftop billboard, rooftop water tank, rooftop air-conditioner rack, bicycle shelter and a garbage station with a lid that opens.
STACKABLE MODULES: build mansions and danchi of any width and height from ground / floor / roof / end pieces (see 'How the modules fit').
A sample town (one street with both sides built up, a back row of tall buildings and two module-built blocks, 88 placed pieces, about 220k triangles) is included as ONE ready-made scene file in scene/ and as a walkable Godot 4 project.
Total 176288 triangles for one of each of the 35 models. Buildings 1,800-37,000 triangles (most under 8,000), props 470-1,900, modules 100-2,400.

- EXTERIOR ONLY: these are background / street-front buildings. They have no interiors and cannot be entered (windows are opaque: dark glass, or curtains that light up at night). For walk-in buildings see our building kits (house, school, inn).
- Units: meters, real-world scale. Origin = front-left corner of the plot at ground level (z = 0). The building stands on y 0 .. depth (Blender), front facing -Y (glTF / Godot +Z). Awnings, signs and canopies may hang up to 1 m over the front line (onto the sidewalk), roof eaves and balcony slabs a few cm past the plot edge. Every building has a 0.3 m foundation BELOW z = 0, so it still meets the ground on slightly uneven terrain.
- Plot sizes are multiples of 0.5 m, so the buildings line up on a 0.5 m grid and fit the same street grid as our Japanese Street / road packs (2-lane road 10 m wide incl. sidewalks: put the building origins 5 m from the road centre line).
- Materials: `palette32` (ONE 512x256 palette texture with 3-tone shading baked in; use nearest / point filtering), `palette32_WindowGlow` (same texture: curtained windows, turn on emission at night), `Kobo_Emissive` (same texture, glows: signs, lamps, warning lights), `Kobo_Glass` (transparent glass: tower balcony parapets), `Poster_Paper` (blank billboard face: swap in your own picture), `Kobo_Shadow` (optional soft contact shadow plane under props).
- Transforms applied.
- FBX files embed the textures (also in palette/). Exported from Blender 5.1: 1 unit = 1 m, Y up.
- No logos, no brand names, no readable text (signs and window letters are abstract marks, the billboard is blank). The danchi carry building numbers (3, 12) and the parking garage a 'P' sign.
- Clean everyday look: no stains, no rust.
- Made with AI assistance (CreatedWithAI). Checked in Godot 4.7.2. Unity: tested in Unity 6.6 (see the Unity section). Unreal: standard FBX, not tested there.

## Downloads: one zip per format
This pack is delivered as 3 separate zip files. **This zip contains the FBX version only.** Download the other zips from the same store page if you need another format (you do not need all of them).

| Zip | Contains |
| --- | --- |
| `LowPolyJapaneseTownBuildings_GLB_v1.2.1.zip` | GLB models (glTF binary) + the ready-to-run Godot 4 sample project |
| `LowPolyJapaneseTownBuildings_FBX_v1.2.2.zip` | FBX models |
| `LowPolyJapaneseTownBuildings_Blend_v1.2.1.zip` | Blender (.blend) files |

The same README is in each model zip.

## Night windows
- Godot sample: N switches day / night; scripts/daynight.gd turns on emission for every material whose name ends in `_WindowGlow`.
- Unity / Unreal / Blender: give `palette32_WindowGlow` an emission (same texture as emission map, strength 1-3) for night scenes, keep it at 0 by day. `Kobo_Emissive` signs can stay lit all the time.

## How the modules fit
- MANSION modules: bay width 6.0 m, depth 12.0 m incl. balconies and the back corridor. `mansion_module_ground` at z = 0, `mansion_module_floor` at z = 0.45 + n x 2.9 (n = 1, 2, 3 ...), `mansion_module_roof` at z = 0.45 + floors x 2.9. Put the bays side by side every 6.0 m along +X. Close the left end with `mansion_module_end_l(_ground)` at the first bay's origin and the right end with `mansion_module_end_r(_ground)` at the last bay's origin + 6.0 m (end walls sit just outside the bays).
- DANCHI modules: one stair module is 13.5 m wide (two flats + the stair hall), depth 9.0 m incl. balconies. `danchi_module_ground` at z = 0, `danchi_module_floor` at z = 0.6 + n x 2.7, `danchi_module_roof` at z = 0.6 + floors x 2.7, modules every 13.5 m; gables `danchi_module_end_l(_ground)` at the first origin, `_end_r(_ground)` at the last origin + 13.5 m.
- Example (in the sample town): 4 mansion bays x 6 storeys and 2 danchi modules x 6 storeys.
- Rooftop props: put `rooftop_*` on any flat roof; the billboard's face is `Poster_Paper` with a `TextArea_01` marker.

## Moving parts, markers, collision
- `garbage_station`: the lid is a SEPARATE child object with its origin on the hinge (back top edge); it opens upward 75 degrees. The Godot sample opens it with E or a click. See PIVOTS.txt.
- Empty nodes: `Light_NN` (put a light here: billboard lamps, shelter lamp, entrances), `TextArea_NN` (blank area for your own text / picture; custom properties kobo_w / kobo_h give its size in metres).
- `<name>_shadow` = a 2-triangle soft shadow plane under small props (material Kobo_Shadow, palette/shadow_blob.png). Delete it if you use real-time shadows only.
- FBX: simple collision is included as `UCX_<name>_NN` (Unreal picks it up on import; in Unity the included import helper turns it into hidden convex Mesh Colliders): one box per building (footprint x height), one per module and per prop (the water tower has two).

## Three colours
- Default files = A.
- `<name>_b` and `<name>_c` = B and C: other wall / roof / balcony colours for 11 buildings (danchi_5f, house_modern_2f, house_tile_2f, kanban_shop_2f, mansion_10f, mansion_5f, office_building_8f, pencil_building_6f, shop_house_3f, tenant_building_7f, wooden_apartment_2f). Props and modules have one colour.

## Contents

### Apartments
- Apartment Building 5F (mansion, 3 units wide, balconies front, corridor back) — `tb_mansion_5f_01` (21.1 x 12.3 x 18.6 m, 14280 tris)
- Apartment Building 10F (mansion, 4 units wide) — `tb_mansion_10f_01` (27.1 x 12.3 x 33.1 m, 37144 tris)
- Tower Apartment 24F (glass balconies, lobby, crown) — `tb_tower_mansion_24f_01` (24.0 x 24.0 x 82.1 m, 27260 tris)
- Danchi Housing Block 5F (3 stair modules, building number on gable) — `tb_danchi_5f_01` (40.6 x 10.0 x 17.3 m, 28460 tris)
- Danchi Housing Block 4F (2 stair modules) — `tb_danchi_4f_01` (27.1 x 10.0 x 14.5 m, 15732 tris)
- Wooden Apartment 2F (outside steel stair, 6 rooms) — `tb_wooden_apartment_2f_01` (12.3 x 9.0 x 7.9 m, 7630 tris)

### Commercial
- Pencil Building 6F (narrow, vertical sign) — `tb_pencil_building_6f_01` (5.1 x 13.3 x 22.0 m, 3454 tris)
- Tenant Building 7F (window letters, back steel stair) — `tb_tenant_building_7f_01` (8.1 x 17.2 x 25.8 m, 6164 tris)
- Office Building 8F (ribbon windows, entrance canopy) — `tb_office_building_8f_01` (16.1 x 15.8 x 33.0 m, 7508 tris)
- Shop House 3F (shop + home above, awning) — `tb_shop_house_3f_01` (7.0 x 11.2 x 12.1 m, 2772 tris)
- Kanban-Style Shop 2F (flat facade old shop) — `tb_kanban_shop_2f_01` (7.0 x 11.3 x 8.2 m, 2208 tris)
- Warehouse / Workshop (roll-up shutter, roof vents) — `tb_warehouse_workshop_01` (12.9 x 17.2 x 8.4 m, 4564 tris)
- Parking Garage 3F (open decks, stair tower, entry barrier) — `tb_parking_garage_3f_01` (16.0 x 32.3 x 12.1 m, 1904 tris)

### Houses
- House 2F – tiled roof — `tb_house_tile_2f_01` (10.2 x 9.2 x 10.5 m, 2952 tris)
- House 2F – modern box with solar panels — `tb_house_modern_2f_01` (7.6 x 10.5 x 7.0 m, 1788 tris)

### Props
- Danchi Water Tower — `tb_danchi_water_tower_01` (9.9 x 9.9 x 27.0 m, 1888 tris)
- Rooftop Billboard (blank poster, lights) — `tb_rooftop_billboard_01` (6.2 x 2.8 x 4.8 m, 472 tris)
- Rooftop Water Tank — `tb_rooftop_water_tank_01` (1.8 x 2.1 x 3.2 m, 502 tris)
- Rooftop Air-Conditioner Rack — `tb_rooftop_ac_rack_01` (4.1 x 1.0 x 1.0 m, 598 tris)
- Bicycle Shelter with bike racks — `tb_bicycle_shelter_01` (6.0 x 2.1 x 2.4 m, 602 tris)
- Garbage Station (lid opens) — `tb_garbage_station_01` (2.1 x 1.1 x 1.1 m, 1542 tris, 1 moving part)

### Stackable modules
- Mansion Module – ground floor bay 6 m — `tb_mansion_module_ground_01` (6.0 x 12.0 x 3.6 m, 908 tris)
- Mansion Module – upper floor bay 6 m — `tb_mansion_module_floor_01` (6.0 x 12.0 x 3.1 m, 908 tris)
- Mansion Module – roof bay 6 m — `tb_mansion_module_roof_01` (6.0 x 12.0 x 1.0 m, 144 tris)
- Mansion Module – left end wall (upper floor) — `tb_mansion_module_end_l_01` (0.2 x 12.0 x 2.9 m, 102 tris)
- Mansion Module – right end wall (upper floor) — `tb_mansion_module_end_r_01` (0.2 x 12.0 x 2.9 m, 102 tris)
- Mansion Module – left end wall (ground floor) — `tb_mansion_module_end_l_ground_01` (0.2 x 12.0 x 3.6 m, 102 tris)
- Mansion Module – right end wall (ground floor) — `tb_mansion_module_end_r_ground_01` (0.2 x 12.0 x 3.6 m, 102 tris)
- Danchi Module – ground floor stair module 13.5 m — `tb_danchi_module_ground_01` (13.5 x 10.0 x 3.6 m, 2056 tris)
- Danchi Module – upper floor stair module 13.5 m — `tb_danchi_module_floor_01` (13.5 x 9.3 x 4.1 m, 1864 tris)
- Danchi Module – roof 13.5 m — `tb_danchi_module_roof_01` (13.5 x 9.0 x 1.4 m, 120 tris)
- Danchi Module – left gable (upper floor) — `tb_danchi_module_end_l_01` (0.2 x 9.0 x 2.7 m, 114 tris)
- Danchi Module – right gable (upper floor) — `tb_danchi_module_end_r_01` (0.2 x 9.0 x 2.7 m, 114 tris)
- Danchi Module – left gable (ground floor) — `tb_danchi_module_end_l_ground_01` (0.2 x 9.0 x 3.6 m, 114 tris)
- Danchi Module – right gable (ground floor) — `tb_danchi_module_end_r_ground_01` (0.2 x 9.0 x 3.6 m, 114 tris)

## Works with
Same palette and scale as the other Kobo Assets packs: Japanese Street, Konbini, Station, Japanese House Building Kit, School Building Kit, Town NPCs, Town Cars, Yokai characters.

## Version history
- 1.0: first release (with LODs).

## LODs (lighter versions for far away)
- 41 heavier models come with LOD1 (and most with LOD2): about 45 % / 53 % fewer triangles in total, same palette, same origin and size, same look from a distance.
- Made by removing faces nobody can see from outside (inside walls, backs of boards), turning bevelled boxes into plain boxes and merging flat areas; colour borders are kept. Every level was checked against the original from 6 views at the size it is seen at. Collision stays on the full model.
- Suggested switch: LOD1 below 25 % of screen height, LOD2 below 10 %. Full list: LODs/LOD_LIST.txt.
- Unity: drag LODs/Unity_LODGroup/<model>_LODs.fbx into the project – the objects are named _LOD0/_LOD1/_LOD2, so Unity builds the LOD Group by itself (static single-mesh models).
- Unreal Engine: import the normal model, then Static Mesh Editor > LOD Settings > LOD Import > 'Import LOD Level 1' / 'Level 2' and pick LODs/<model>_LOD1.fbx / _LOD2.fbx.

## Unity
Unity ready: tested in Unity 6.6 (6000.6), URP and the Built-in render pipeline.
1. Copy the whole `LowPolyJapaneseTownBuildings/` folder into your project's `Assets/` folder (not just the FBX files). It brings `models/`, `palette/` (or `textures/`) and the import helper `Unity/Editor/KoboFbxPostprocessor.cs` along.
2. That's it. Unity compiles the small helper (Editor only, it is not part of your build) and sets every model up on import:
   - assigns the palette / face textures and turns off mipmaps and compression on them (keeps the flat colours crisp at any distance),
   - makes `*_Emissive` / `Kobo_Emissive` glow; `palette32_WindowGlow` gets the emission map at 0 - raise its Emission colour (about 0.8-1.5) for night scenes,
   - makes glass and blob shadows transparent (blob shadows cast no shadow),
   - turns every `UCX_` mesh into a convex Mesh Collider and hides it,
   - renames character clips to `Idle`, `Walk`, ... and sets Idle / Walk / Run / Walk_Back / Jump_Air / Talk / Sit to loop.
If the models were imported before the helper, right-click the pack folder > Reimport.
Own several Kobo packs? Copy them all: each pack's helper sits in its own Editor assembly (`.asmdef`), so the copies do not clash.
Without the helper the models still import at the right scale (1 unit = 1 m), but the textures are not assigned (assign the PNG from `palette/` or `textures/` to the materials by hand) and the `UCX_` boxes are visible (delete them or disable their renderers).
Rigged characters import as Generic rigs; use an Animator Controller with the clips from the FBX.

## License (for buyers)
A short summary of what you may do with this pack. The licence of the store you bought it from (on Fab: the Fab Standard License / EULA) is the binding one and wins if anything here differs.

You may:
- use the assets royalty-free in personal and commercial projects: games, apps, VR/AR, films and videos, renders, streams, prototypes and client work;
- edit, recolour, cut apart and combine them with other assets;
- distribute them as part of a finished game, app, video or render (inside that product, not as separate asset files for others to reuse).

You may not:
- resell, share, give away or re-upload the asset files themselves, original or modified: not on their own, not inside another asset pack, template, kit or library, and not in a public repository;
- present the assets as your own work for sale as assets.

Created with AI-assisted tools.
Credit ("Assets by Kobo Assets") is welcome but not required.
