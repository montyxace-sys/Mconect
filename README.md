# Mnet

Escritorio remoto directo entre dos equipos Windows, con una interfaz sencilla inspirada en TeamViewer. Sin cuentas, sin servidores intermediarios y sin datos en la nube: los dos equipos se conectan entre sí por la red local o por una IP pública.

![Ventana principal de Mnet](docs/main.png)

## Funciones

- Control real del ratón y del teclado del equipo remoto.
- Imagen de pantalla cifrada con AES-256-GCM y clave de sesión efímera (ECDH P-256 + HKDF-SHA256).
- Contraseña de sesión comprobada con HMAC-SHA256 en tiempo constante.
- Consentimiento explícito: la otra persona debe aceptar cada conexión; el aviso se rechaza solo a los 60 segundos.
- Pantalla completa con F11 y Salir con Esc.
- ID local visible y copiable, contraseña aleatoria y botón para generar una nueva.
- Conexión directa por IP y puerto (por defecto el 52222), sin relay ni registro.

## Cómo se usa

1. Abre Mnet en el equipo que se va a controlar y pulsa **Permitir conexiones**. Aparecerán tu ID y tu contraseña.
2. Comparte esos dos datos con la otra persona.
3. En el otro equipo escribe el ID (`192.168.1.20` o `192.168.1.20:52222`) y la contraseña, y pulsa **Conectar**.
4. En el equipo controlado aparece una solicitud de consentimiento. Si la aceptas, se abre la ventana de sesión.
5. Para terminar, pulsa **Finalizar** en cualquiera de los dos equipos.

La primera vez, Windows Defender Firewall preguntará si permites Mnet en la red. Acepta al menos para redes privadas.

## Seguridad

- La contraseña nunca viaja por la red: se demuestra su conocimiento con un HMAC sobre un secreto compartido derivado del intercambio ECDH.
- Todo el contenido de la sesión (pantalla, ratón, teclado) viaja cifrado y autenticado con AES-256-GCM.
- Una contraseña incorrecta cierra la conexión antes de mostrar nada del escritorio.
- El anfitrión siempre decide si acepta cada conexión.
- No hay cuentas, analítica ni servidores de MSoft implicados.

## Limitaciones de esta versión (0.1)

- Solo Windows 10/11 de 64 bits; el equipo que controla también debe ser Windows.
- Un monitor (el principal) y sin captura del cursor.
- Conexión por IP: no hay directorio de IDs ni NAT traversal. Para usarlo por internet hay que abrir el puerto 52222 en el router del anfitrión o usar una VPN.
- Sin audio, portapapeles compartido, transferencia de archivos ni varias sesiones a la vez.
- Ctrl+Alt+Supr no se puede enviar (limitación de Windows).

## Compilar

Requisitos: Windows y el SDK de .NET 8.

```
dotnet build Mnet.sln -c Release
```

Para generar un único ejecutable autocontenido (no necesita .NET instalado):

```
dotnet publish App/Mnet.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Pruebas

```
dotnet run --project tests/Mnet.CoreTests -c Release
```

Las pruebas cubren el cifrado de tramas, el saludo, la autenticación con contraseña correcta e incorrecta, la serialización de mensajes y una sesión completa por TCP local con pantalla, ratón, teclado y cierre.

## Licencia

MIT © 2026 MSoft.
