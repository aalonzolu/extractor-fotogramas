# Extractor de Fotogramas

Programa para Windows que extrae fotogramas de un video a imágenes PNG y genera un reporte con los hashes SHA-256 y SHA-512 del video original y de cada fotograma. Es portable: no requiere instalación y trae FFmpeg incluido.

## Descarga

Descarga el archivo `ExtractorFotogramas-<versión>-portable.zip` de la [última versión](https://github.com/aalonzolu/extractor-fotogramas/releases/latest), descomprímelo y abre `ExtractorFotogramas.exe`.

Funciona en Windows 8, 8.1, 10 y 11, que ya traen .NET Framework 4.5 o superior.

## Qué hace

- Extrae momentos sueltos, rangos `desde – hasta` o todo el video. Los tiempos se escriben como en el reproductor (horas, minutos, segundos) y se validan contra la duración del video.
- De cada segundo `T` extrae todos sus fotogramas, del instante `T` hasta antes de `T+1`.
- Guarda cada extracción en una carpeta nueva, `Fotogramas_<video>_<epoch>`, junto al video original o en la carpeta que se elija. Si el video está en un disco de solo lectura (DVD, bloqueador de escritura), lo detecta y pide elegir otra carpeta.
- El reporte `hashes_fotogramas.txt` incluye SHA-256 y SHA-512 del video y de cada fotograma, el formato del video, los tiempos solicitados y la versión y parámetros de FFmpeg usados.
- Antes de extraer estima cuántos fotogramas saldrán y cuánto espacio ocuparán, y no deja empezar si no hay espacio suficiente en el disco.
- Videos con datos dañados: decodifica con `-flags2 +showall` para recuperar los fotogramas en vez de descartarlos (no altera videos sanos), cuenta los errores de decodificación y compara los fotogramas extraídos con los del archivo. Todo queda anotado en el reporte.
- Flujos de video sin contenedor (`.h264`, `.h265` exportados de DVR): como no indican su velocidad y FFmpeg asume 25 fps, el programa pide la velocidad real y estima la duración contando paquetes.
- Detecta fotogramas sobreescritos por marcas de tiempo repetidas (videos de frecuencia variable).
- Soporta rutas de más de 260 caracteres (vía `\\?\` en Win32), sin renombrar el video.

## Compilar

Requiere el .NET SDK 6 o superior; compila desde Windows, macOS o Linux.

```sh
dotnet build src -c Release -o build
```

El programa busca FFmpeg en este orden: `ffmpeg\`, `ffmpeg\bin\`, junto al `.exe` y por último en el `PATH`.

## Publicar una versión

El workflow [`release.yml`](.github/workflows/release.yml) compila el programa, descarga FFmpeg, arma el ZIP portable y lo publica como release. Se ejecuta al subir una etiqueta de versión:

```sh
git tag v1.6.1
git push origin v1.6.1
```

También se puede lanzar a mano desde la pestaña **Actions**, indicando la versión.

## Contribuir

Las contribuciones son bienvenidas: abre un *issue* para reportar un problema o proponer una mejora, o envía un *pull request* con el cambio.

## Licencia

El código de este programa se distribuye bajo la [licencia MIT](LICENSE): se puede usar, modificar y redistribuir libremente, también con fines comerciales, conservando el aviso de copyright.

El ZIP portable incluye `ffmpeg.exe` y `ffprobe.exe` de la compilación *essentials* de [gyan.dev](https://www.gyan.dev/ffmpeg/builds/), que son programas aparte distribuidos bajo su propia licencia GPL (incluida en `ffmpeg/LICENSE.txt`). El código fuente de FFmpeg está en [ffmpeg.org](https://ffmpeg.org/).
