# MovU — Videojuego serio de orientación 3D

Proyecto de grado · Ingeniería de Sistemas · Pontificia Universidad Javeriana
Director: Ing. Leonardo Flórez Valencia
Equipo: Damián Rey (diseño de juego y misiones) · Juan Diego Palacios (QA y métricas) · Sergio Parra (desarrollo)

MovU es un juego de orientación en primera persona ambientado en el edificio de Ingeniería.
El jugador recorre el edificio buscando un objetivo, con una flecha de guía que se puede
apagar o encender, mientras el juego registra en CSV qué tan eficiente fue su recorrido.
Ese registro es la evidencia experimental de la tesis.

---

## 1. Estado actual (14 de septiembre de 2026)

| Parte | Estado |
|---|---|
| Mecánicas base (misiones, flecha de guía, métricas CSV, récord por semilla) | ✅ Funcionando en `DemoMaze` |
| Personaje en 1ª / 3ª persona (`CameraRig`) | ✅ |
| Planta caminable a partir del modelo de Meshy | ✅ |
| Shader estilizado low-poly sin UVs | ✅ |
| Edificio de 3 pisos + ascensor + carga por piso | ⚠️ **Escrito pero SIN compilar todavía** |
| Occlusion Culling horneado | ❌ Pendiente |
| NavMesh con varios pisos | ❌ Pendiente (hoy solo hay uno) |
| Flecha de guía entre pisos | ❌ Pendiente — apunta en línea recta a través del techo |
| Métricas de cambio de piso | ❌ Pendiente |
| Escala definitiva del edificio | ⚠️ Por decidir (hoy 56x horizontal / 45x vertical) |

**Lo primero que hay que hacer al abrir el proyecto es compilar.** Los scripts de
`Scripts/Building/`, `Editor/BuildingSetup.cs` y los dos cambios del shader se escribieron
sin poder abrir Unity, así que es donde más probable es que haya un error.

Último commit: `sanguche` (9 de septiembre). Tres commits en total, rama `main`.

### Decisiones ya tomadas (no volver a discutirlas sin motivo)

- **El mapa jugable es el modelo ORIGINAL de Meshy**, no el reconstruido. Se conserva su
  geometría orgánica a la altura de los ojos; los muros cortos se tapan por encima con una
  malla aparte (`RellenoCorona.obj`).
- **Escala no uniforme a propósito:** 56x en horizontal, 45x en vertical. Escalar todo a 56x
  dejaría pasillos de 7 m de alto — catedral, no edificio universitario.
- **Tres pisos = el mismo piso apilado tres veces**, porque todavía no hay planos de los otros
  niveles. Se cambia de piso con un cubo interactuable que abre un panel.
- **Cada piso es autónomo.** `FloorManager` apaga los pisos que nadie está viendo, así que un
  piso no puede depender de que el vecino esté cargado (por eso cada piso trae su propia losa
  de techo). Cualquier cosa que se añada después tiene que cumplir esta regla.

---

## 2. Requisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| Unity | **6000.3.21f1** (Unity 6.3 LTS) | El proyecto. No abrirlo con otra versión sin avisar al equipo |
| URP | 17.3.0 (ya en `manifest.json`) | Render pipeline |
| Input System | 1.20.0 — **el nuevo**, no el viejo | Todo el input está escrito con `Keyboard.current` |
| AI Navigation | 2.0.14 | `NavMeshSurface` (el sistema nuevo, no "Navigation Static") |
| Git LFS | cualquiera reciente | `*.obj`, `*.fbx`, `*.blend`… están en LFS (ver `.gitattributes`) |
| Python 3 + `numpy` | 3.10+ | Scripts de `Tools/` que generan las mallas |
| Blender (opcional) | 4.x | Solo para `blender_pulir_plano.py` |

**Dónde clonar:** en una carpeta que **no** esté sincronizada con OneDrive / Drive / iCloud.
La carpeta `Library/` de Unity se reescribe constantemente y la sincronización la corrompe.
La ruta que se usa hoy es `C:\TesisProyecto\MovU_Demo`.

---

## 3. Cómo correr el proyecto

### 3.1 Primera vez

```bash
git lfs install
git clone https://github.com/juanDiesmers/MovU_Demo.git C:\TesisProyecto\MovU_Demo
cd C:\TesisProyecto\MovU_Demo
git lfs pull            # sin esto los .obj llegan como punteros de texto de 130 bytes
pip install numpy
```

Abrir el proyecto con Unity **6000.3.21f1**. La primera importación tarda varios minutos
(reimporta todos los modelos). Si Unity pide importar **TextMeshPro Essential Resources**,
aceptar — sin eso el HUD no dibuja texto.

### 3.2 Generar las mallas auxiliares

`RellenoCorona.obj` está versionado, pero si se cambia la altura del techo o el modelo base
hay que regenerarlo:

```bash
python3 Tools/tapar_corona.py                       # valores por defecto: muro a 3,49 m
python3 Tools/tapar_corona.py --altura-muro 3.49 --banda 0.25
```

Otros scripts, solo si hacen falta:

| Script | Qué produce | Cuándo |
|---|---|---|
| `Tools/tapar_corona.py` | `RellenoCorona.obj` — sube los muros cortos + losa de techo | Siempre que cambie la altura del edificio |
| `Tools/reconstruir_planta.py` | `PlantaMovU_Reconstruida.obj` — planta reconstruida a 8k triángulos | **Descartado** como mapa jugable; se conserva como evidencia |
| `Tools/preparar_malla.py` | `PlanoMovU_Estilizado.obj` — el mismo modelo con UVs | Solo si se quieren texturas PBR reales |
| `Tools/blender_pulir_plano.py` | `PlanoMovU_Pulido.obj` — normales hacia afuera + bisel | Solo si se quiere volver a back-face culling |

### 3.3 Armar la escena (menú `MovU` en la barra de Unity)

**Orden importa.** Todos los menús son idempotentes: se pueden correr las veces que sea.

```
1. MovU > Preparar plano Meshy jugable          → crea el Player, el punto de aparición y los colliders
2. MovU > Estilizar entorno > Limpio (low-poly) → material, luces y niebla
3. MovU > Edificio > Construir edificio de 3 pisos → apila los pisos, pone el ascensor y el FloorManager
4. Ctrl+S
5. Window > Rendering > Occlusion Culling > Bake  (a mano, todavía no está automatizado)
```

Menús adicionales:

- `MovU > Edificio > Cargar tambien los pisos vecinos` — para depurar: mantiene 3 pisos activos.
- `MovU > Estilizar entorno > Anadir suelo exterior` — plano de horizonte sin collider.
- `MovU > Estilizar entorno > Alternar culling de caras` — `Off` / `Back`.
- `MovU > Construir escena demo` — reconstruye el laberinto del demo (`DemoMaze`).
- `MovU > Generar Build de Windows`.

Después de construir, la consola imprime las medidas reales:
`[Edificio] Planta de 39,4 x 106,8 m. Alto de un piso: ... -> separacion ...`.
Si esos números se ven raros, la escala está mal antes de que el juego siquiera corra.

### 3.4 Controles

| Tecla | Acción |
|---|---|
| `W A S D` | Caminar (3 m/s) |
| Ratón | Mirar |
| `V` | Alternar primera / tercera persona |
| `G` | Ciclar el modo de la flecha de guía (Off → Direct → NavMesh) |
| `E` | Abrir el panel del ascensor (estando junto al cubo) |
| `1`…`9` | Elegir piso con el panel abierto |
| `Esc` | Cerrar el panel · soltar/capturar el cursor |
| `R` | Reiniciar la corrida (registra la anterior como abortada) |

### 3.5 Dónde quedan las métricas

`%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\movu_metrics.csv`

En Unity: `Application.persistentDataPath` + `movu_metrics.csv`. Se escribe con
`InvariantCulture` (punto decimal), así que abre bien en cualquier Excel.
Columnas: tiempo, distancia total, distancia por modo de guía, ruta óptima, detour ratio, SPL.

---

## 4. Estructura del repositorio

```
Assets/MovU/
  Scenes/DemoMaze.unity         Escena principal (escena 0 en Build Settings)
  Scripts/
    Core/      GameManager, DemoConfig
    Maze/      MazeGenerator, MazeGrid, MazeData      (laberinto del demo, con semilla)
    Player/    PlayerController, MouseLook, CameraRig
    Mission/   MissionManager, Objective, ObjectiveTrigger
    Guidance/  GuidanceArrow, GuidanceMode
    Data/      MetricsLogger, RunMetrics, SaveSystem
    UI/        HUDController
    Building/  FloorManager, ElevatorTrigger, ElevatorPanel   ← sin compilar aún
  Editor/
    DemoSceneBuilder.cs     construye el laberinto del demo
    MeshiWalkableSetup.cs   deja el plano de Meshy caminable
    EnvironmentStyler.cs    presets de estilo, luces y niebla
    BuildingSetup.cs        edificio de 3 pisos, ascensor, FloorManager
  Shaders/StylizedEnvironment.shader    shader URP que no necesita UVs
  Models/    OBJ del plano y mallas generadas
  Materials/
Tools/       scripts de Python que generan las mallas
_ModelosCrudos/   fuera de Assets, ignorado por git
```

**Arquitectura:** Manager Pattern + Observer (eventos de C#). 1 unidad de Unity = 1 metro.
Objetivo de rendimiento: 60+ FPS.

---

## 5. Errores conocidos y cómo se arreglan

### Al importar / abrir

**El modelo aparece minúsculo (o 1.000× más grande).**
Es `Convert Units` del importador de OBJ. No se arregla con la escala del Transform: hay que
poner el **Scale Factor en el importador del asset**. Con archivos DWG pasa lo mismo si vienen
en milímetros: verificar unidades **antes** de cualquier otra cosa.

**Los `.obj` pesan 130 bytes y Unity no los abre.**
Falta `git lfs pull`. Lo que se descargó son punteros de LFS, no las mallas.

**Errores de compilación raros después de un `pull`.**
Cerrar Unity, borrar `Library/`, volver a abrir. Tarda, pero resuelve el 90% de los casos.

**El HUD no muestra texto.**
Falta importar TextMeshPro Essential Resources (`Window > TextMeshPro > Import TMP Essential Resources`).

### Al armar la escena

**"No hay un Player en la escena" al construir el edificio.**
`MovU > Edificio` necesita que ya exista el jugador. Correr antes
`MovU > Preparar plano Meshy jugable`.

**"Falta Assets/MovU/Models/RellenoCorona.obj".**
Correr `python3 Tools/tapar_corona.py`. Sin esa malla, al apilar los pisos se ve el piso de
arriba por encima de cada muro que se quedó corto.

**El entorno se ve blanco y liso, sin baldosas ni zócalo.**
Unity importó los materiales planos del `.mtl` del OBJ y quedaron pegados a las ranuras.
`EnvironmentStyler` ya pone `materialImportMode = None`; volver a correr
`MovU > Estilizar entorno > Limpio (low-poly)`.

**Huecos negros al mirar ciertos muros.**
El OBJ de Meshy trae caras con el giro invertido. El material va con culling apagado a
propósito. Si alguien lo pone en `Back`, vuelven los huecos:
`MovU > Estilizar entorno > Alternar culling de caras`. Se arregla de verdad pasando el modelo
por `Tools/blender_pulir_plano.py` (recalcula las normales hacia afuera).

**Los muros se ven "derretidos".**
Ángulo de suavizado del importador. `EnvironmentStyler` lo baja a 35°; con el 60° por defecto
una malla decimada pierde todas las esquinas.

### En ejecución

**Los botones del panel del ascensor no responden (pero las teclas 1..9 sí).**
El proyecto usa el Input System nuevo: el `EventSystem` tiene que llevar
`InputSystemUIInputModule`, no `StandaloneInputModule`. `ElevatorPanel` lo crea si falta; si
alguien dejó un EventSystem viejo en la escena, hay que borrarlo.

**El jugador aparece dentro de un muro o se cae al vacío al cambiar de piso.**
`FloorManager.LlevarAlPiso()` activa el piso destino **antes** de mover al jugador y desactiva
el `CharacterController` durante el movimiento. Si se toca ese orden, el jugador cae sobre
colisiones que todavía no existen, o el `CharacterController` reescribe la posición en su
propio `Update`.

**El interior está oscuro.**
Con el techo cerrado el sol ya no entra. La iluminación viene de `_CeilingEmission` del shader
(el techo hace de luminaria) más luz ambiental alta y niebla corta (18–70 m). Lo configura
`BuildingSetup`; si se reemplaza el material a mano, se pierde.

**No se ve el techo / se ve el piso de arriba.**
Cada piso tiene que traer su propia losa de techo. Si alguien "optimiza" usando la losa del
piso superior como techo del inferior, al apagarse ese piso quedan todos a cielo abierto.

**`¡CRÍTICO! NavMesh NO disponible` en consola.**
`DemoSceneBuilder` llama a `BuildNavMesh()` sin crear un asset de `NavMeshData`, así que el
NavMesh puede no persistir al guardar la escena. Rehornear el `NavMeshSurface`, y si el
problema se repite, persistir el `NavMeshData` como asset. En Unity 6.3 es
**`NavMeshSurface`**, no el viejo "Navigation Static".

**FPS bajos / el horneado de colisión tarda una eternidad.**
Números de referencia por piso: 94.440 triángulos de render, 78.878 de colisión. Con los tres
pisos cargados a la vez son 283.320. Si va lento: confirmar que `FloorManager` está dejando un
solo piso activo (`pisosVecinosCargados = 0`) y que el Occlusion Culling está horneado.

### Git

**Conflictos imposibles en `DemoMaze.unity`.**
La escena está guardada en **binario**. Git no puede hacer diff ni merge de ella, y son tres
personas en el repo. Hay que pasarla a serialización de texto
(`Edit > Project Settings > Editor > Asset Serialization: Force Text`). Mientras tanto:
**una sola persona toca la escena a la vez**.

**Se gasta la cuota de Git LFS.**
`*.obj` está en LFS. Antes de hacer commit, borrar lo que ya no se usa (ver la sección de
limpieza). Los modelos crudos van en `_ModelosCrudos/`, que está en `.gitignore`.

---

## 6. Qué falta (en orden)

1. **Compilar** los scripts de `Building/`, `BuildingSetup.cs` y el shader. Nada de eso se ha
   abierto nunca en Unity.
2. **Decidir la escala definitiva** del edificio, ya con el techo puesto (un pasillo techado
   se lee mucho más pequeño que el mismo pasillo a cielo abierto).
3. **Hornear el Occlusion Culling.** La geometría ya está marcada como
   `OccluderStatic | OccludeeStatic`; falta el bake. Sin eso, media optimización no existe.
4. **NavMesh por piso.** Hace falta un `NavMeshSurface` en cada piso.
5. **Flecha de guía entre pisos.** Hoy `GuidanceArrow` no sabe que existen pisos: si el
   objetivo está arriba, apunta en línea recta a través del techo. Es justamente el tema de la
   tesis, así que no es un detalle.
6. **Métricas de cambio de piso.** `MetricsLogger` no registra cuántas veces el jugador se
   equivocó de nivel — probablemente la variable más interesante del experimento.
7. **Puertas y marcos** (kits CC0 de Kenney) una vez validado el material base.
8. **SSAO** en el renderer de URP. El pase `DepthNormals` ya está en el shader; falta activarlo.
   Es la mejora de aspecto con mejor relación esfuerzo/resultado que queda.
9. **Limpieza antes del próximo commit:**
   - `Assets/MovU/Models/PlanoMovU_Estilizado.obj` (9,5 MB) — sin uso
   - `Assets/MovU/Models/PlantaMovU_Reconstruida.obj` (1,3 MB) — descartado como mapa jugable
   - `_Previews/` y `planta_reconstruida.png` en la raíz — son diagnósticos; el mapa de alturas
     sirve como figura del documento de grado, pero no debería versionarse así

---

## 7. Recursos externos

Todos CC0 (dejar constancia de la fuente en el documento igual):

- [Kenney](https://kenney.nl/assets/category:3D) — `Furniture Kit`, `Building Kit`, `Modular Buildings`
- [Quaternius](https://quaternius.com/) — `Ultimate House Interior Pack`
- [Poly Pizza](https://poly.pizza/) — piezas sueltas de los dos anteriores
- [ambientCG](https://ambientcg.com/) — texturas PBR (`Tiles012`, `Concrete010`, `Concrete012`)
- [3D Textures](https://3dtextures.me/) — concreto y baldosa
