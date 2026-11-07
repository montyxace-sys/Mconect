using Mnet.Core.Protocol;

namespace Mnet.Core.Remoting;

/// <summary>Fotograma listo para enviar: JPEG comprimido y geometría de origen.</summary>
public sealed record ScreenFrame(
    int FrameWidth,
    int FrameHeight,
    int ScreenWidth,
    int ScreenHeight,
    byte[] Jpeg);

/// <summary>Origen de imagen del anfitrión. Captura bajo demanda; puede devolver la misma imagen.</summary>
public interface IRemoteScreen
{
    ScreenFrame Capture();
}

/// <summary>Destino donde el anfitrión inyecta los eventos del visitante.</summary>
public interface IInputSink
{
    void Mouse(MouseMessage message);

    void Key(KeyMessage message);
}
