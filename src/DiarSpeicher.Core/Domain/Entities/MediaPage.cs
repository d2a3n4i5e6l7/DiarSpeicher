namespace DiarSpeicher.Core.Domain.Entities;

/// <summary>
/// Dimensiones y tamaño de una página concreta de un libro.
///
/// Existe porque los clientes de Komga no muestran un libro cuyas páginas no traen
/// <c>width</c> y <c>height</c>: piden la lista, ven las dimensiones a nulo y no llegan a
/// solicitar ninguna imagen. Calcularlas exige abrir el archivo y leer la cabecera de cada
/// página, así que se guardan en lugar de recalcularlas en cada petición.
///
/// No se asume que todas las páginas midan igual: una doble página mide el doble de ancho
/// que el resto, y una portada suele diferir. Por eso hay una fila por página.
/// </summary>
public class MediaPage
{
    public string MediaId { get; set; } = null!;
    public Media Media { get; set; } = null!;

    /// <summary>Número de página, empezando en 1, como lo expone la API.</summary>
    public int Number { get; set; }

    public string FileName { get; set; } = null!;
    public string MediaType { get; set; } = "image/jpeg";

    /// <summary>Nulo cuando la imagen no se pudo decodificar; el resto de páginas se guarda igual.</summary>
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long? SizeBytes { get; set; }
}
