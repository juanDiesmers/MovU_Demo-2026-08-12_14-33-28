# ============================================================================
# blender_pulir_plano.py — MovU
# ============================================================================
# Lo que Blender hace y un script de Python suelto NO puede hacer bien:
#
#   * Recalcular las normales hacia afuera. El OBJ de Meshy tiene caras con el
#     giro invertido; por eso el material del entorno viene con culling apagado.
#     Con las normales arregladas se puede volver a Back face culling, que es
#     mas rapido y evita ver el interior de los muros desde ciertos angulos.
#   * Fusionar vertices duplicados y borrar caras sueltas o degeneradas, que es
#     de donde salen la mayoria de los artefactos de sombreado.
#   * Biselar las aristas. Un bisel de 1-2 cm hace que la luz pegue en el canto
#     de cada muro: es el truco que le da el acabado "limpio" al low-poly.
#
# Ademas repite lo que hace Tools/preparar_malla.py (UVs por proyeccion de caja
# y tres ranuras de material) para que la salida sea intercambiable.
#
# La salida conserva EXACTAMENTE la orientacion del archivo de entrada (arriba
# = +Z, el modelo "de pie"), asi que es un reemplazo directo: MeshiWalkableSetup
# le sigue aplicando la misma rotacion de -90 en X y la escala de 28x.
#
# Uso (sin abrir la interfaz de Blender):
#
#   blender --background --python Tools/blender_pulir_plano.py -- ^
#       --entrada "Assets/MovU/Models/Meshy_AI_Plano_de_evacuación_0831203851_generate.obj" ^
#       --salida  "Assets/MovU/Models/PlanoMovU_Pulido.obj"
#
# En Windows, si 'blender' no esta en el PATH:
#   "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe" --background ...
#
# Probado contra la API de Blender 4.x.
# ============================================================================

import argparse
import math
import os
import sys

import bpy
import bmesh
from mathutils import Vector

GRUPOS = ["Piso", "Muro", "Techo"]
COLORES = {
    "Piso":  (0.64, 0.66, 0.69, 1.0),
    "Muro":  (0.87, 0.87, 0.84, 1.0),
    "Techo": (0.93, 0.93, 0.95, 1.0),
}


def argumentos():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []

    ap = argparse.ArgumentParser()
    ap.add_argument("--entrada", required=True)
    ap.add_argument("--salida",  required=True)
    ap.add_argument("--escala",  type=float, default=28.0,
                    help="Escala que aplica Unity (unidades del archivo -> metros).")
    ap.add_argument("--tile",    type=float, default=1.2,
                    help="Metros de mundo por repeticion de textura.")
    ap.add_argument("--umbral",  type=float, default=0.80,
                    help="Coseno minimo con la vertical para contar como piso/techo.")
    ap.add_argument("--bisel",   type=float, default=0.015,
                    help="Ancho del bisel EN METROS DE MUNDO. 0 lo desactiva.")
    ap.add_argument("--soldar",  type=float, default=0.002,
                    help="Distancia de fusion de vertices EN METROS. 0 lo desactiva.")
    ap.add_argument("--decimar", type=float, default=1.0,
                    help="Proporcion de triangulos a conservar (1.0 = no decimar).")
    return ap.parse_args(argv)


def escena_limpia():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def importar(ruta):
    # up_axis='Z' + forward_axis='Y' = sin conversion de ejes: las coordenadas
    # del archivo entran tal cual, que es lo que queremos para poder devolverlas
    # iguales al exportar.
    bpy.ops.wm.obj_import(filepath=ruta, forward_axis='Y', up_axis='Z')

    objetos = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not objetos:
        raise RuntimeError(f"El archivo no trajo ninguna malla: {ruta}")

    if len(objetos) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for o in objetos:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objetos[0]
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active or objetos[0]
    obj.name = "PlanoMovU"
    return obj


def limpiar_geometria(obj, soldar_m, escala):
    """Fusiona vertices, borra caras degeneradas y recalcula normales hacia afuera."""
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')

    if soldar_m > 0.0:
        # El umbral se da en metros de mundo; el archivo esta en unidades OBJ.
        bpy.ops.mesh.remove_doubles(threshold=soldar_m / escala)

    bpy.ops.mesh.delete_loose()
    bpy.ops.mesh.dissolve_degenerate()
    bpy.ops.mesh.normals_make_consistent(inside=False)   # <- el arreglo clave
    bpy.ops.mesh.quads_convert_to_tris(quad_method='BEAUTY', ngon_method='BEAUTY')

    bpy.ops.object.mode_set(mode='OBJECT')


def decimar(obj, proporcion):
    if proporcion >= 0.999:
        return
    mod = obj.modifiers.new(name="Decimar", type='DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = proporcion
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def biselar(obj, ancho_m, escala):
    if ancho_m <= 0.0:
        return
    mod = obj.modifiers.new(name="Bisel", type='BEVEL')
    mod.width = ancho_m / escala
    mod.segments = 1
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(35.0)
    mod.harden_normals = False
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=mod.name)


def crear_materiales(obj):
    obj.data.materials.clear()
    for nombre in GRUPOS:
        mat = bpy.data.materials.new(name=nombre)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = COLORES[nombre]
            if "Roughness" in bsdf.inputs:
                bsdf.inputs["Roughness"].default_value = 0.85
        obj.data.materials.append(mat)


def asignar_grupos_y_uvs(obj, umbral, tile_m, escala):
    """Clasifica cada cara por su normal, le asigna ranura de material y UVs."""
    metros_por_uv = tile_m / escala

    malla = obj.data
    if not malla.uv_layers:
        malla.uv_layers.new(name="UVMap")

    bm = bmesh.new()
    bm.from_mesh(malla)
    bm.faces.ensure_lookup_table()
    capa_uv = bm.loops.layers.uv.active

    cuenta = [0, 0, 0]

    for cara in bm.faces:
        n = cara.normal
        if n.length == 0.0:
            n = Vector((0.0, 0.0, 1.0))

        arriba = n.z

        if arriba >= umbral:
            grupo = 0                      # Piso
        elif arriba <= -umbral:
            grupo = 2                      # Techo
        else:
            grupo = 1                      # Muro

        cara.material_index = grupo
        cuenta[grupo] += 1

        # Proyeccion de caja: horizontal -> plano XY; muro -> eje horizontal
        # dominante + altura.
        avanza_en_y = abs(n.x) > abs(n.y)
        for loop in cara.loops:
            p = loop.vert.co
            if grupo == 1:
                u = (p.y if avanza_en_y else p.x) / metros_por_uv
                v = p.z / metros_por_uv
            else:
                u = p.x / metros_por_uv
                v = p.y / metros_por_uv
            loop[capa_uv].uv = (u, v)

    bm.to_mesh(malla)
    bm.free()

    total = max(1, sum(cuenta))
    for i, nombre in enumerate(GRUPOS):
        print(f"  {nombre:<6} {cuenta[i]:>8,} caras  ({100.0 * cuenta[i] / total:5.1f} %)")


def exportar(obj, ruta):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

    os.makedirs(os.path.dirname(os.path.abspath(ruta)), exist_ok=True)

    bpy.ops.wm.obj_export(
        filepath=ruta,
        export_selected_objects=True,
        forward_axis='Y', up_axis='Z',     # sin conversion: igual que a la entrada
        export_materials=True,
        export_uv=True,
        export_normals=True,
        export_triangulated_mesh=True,
        apply_modifiers=True,
    )


def main():
    args = argumentos()

    entrada = os.path.abspath(args.entrada)
    salida = os.path.abspath(args.salida)
    if not os.path.isfile(entrada):
        raise SystemExit(f"ERROR: no existe {entrada}")

    print(f"[MovU] Importando {os.path.basename(entrada)} ...")
    escena_limpia()
    obj = importar(entrada)
    print(f"[MovU]   {len(obj.data.vertices):,} vertices / {len(obj.data.polygons):,} caras")

    print("[MovU] Limpiando geometria y recalculando normales ...")
    limpiar_geometria(obj, args.soldar, args.escala)

    if args.decimar < 0.999:
        print(f"[MovU] Decimando al {args.decimar * 100:.0f} % ...")
        decimar(obj, args.decimar)

    if args.bisel > 0.0:
        print(f"[MovU] Biselando aristas ({args.bisel * 100:.1f} cm de mundo) ...")
        biselar(obj, args.bisel, args.escala)

    print("[MovU] Creando materiales y UVs ...")
    crear_materiales(obj)
    asignar_grupos_y_uvs(obj, args.umbral, args.tile, args.escala)

    # Sombreado plano con angulo de division: esquinas nitidas, superficies planas.
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35.0))

    print(f"[MovU] Exportando {os.path.basename(salida)} ...")
    exportar(obj, salida)

    print(f"[MovU] Listo: {len(obj.data.vertices):,} vertices / "
          f"{len(obj.data.polygons):,} caras -> {salida}")


if __name__ == "__main__":
    main()
