import { useEffect, useState } from 'react'
import { api } from '../api/cliente'
import { Cargando } from './ui'

/** Muestra el archivo dentro del navegador (PDF, imágenes, texto).
 *  Se pide como Blob con el token, así el archivo nunca queda expuesto en una URL pública. */
export function Visor({ documentoId, version, tipoMime, alto = '72vh' }: {
  documentoId: string
  version?: number
  tipoMime?: string
  alto?: string
}) {
  const [url, setUrl] = useState<string>()
  const [texto, setTexto] = useState<string>()
  const [error, setError] = useState<string>()
  const mime = (tipoMime ?? '').toLowerCase()
  const visualizable = mime.includes('pdf') || mime.startsWith('image/') || mime.startsWith('text/')
  // TIFF no se muestra en la mayoría de navegadores
  const soportado = visualizable && !mime.includes('tiff')

  useEffect(() => {
    if (!soportado) return
    let creada: string | undefined
    let cancelado = false
    setUrl(undefined); setTexto(undefined); setError(undefined)
    const v = version ? `&version=${version}` : ''
    api.blob(`/api/documentos/${documentoId}/archivo?inline=true${v}`)
      .then(async ({ blob }) => {
        if (cancelado) return
        if (mime.startsWith('text/')) { setTexto(await blob.text()); return }
        creada = URL.createObjectURL(new Blob([blob], { type: mime }))
        setUrl(creada)
      })
      .catch((e) => !cancelado && setError(e.message))
    return () => { cancelado = true; if (creada) URL.revokeObjectURL(creada) }
  }, [documentoId, version, mime, soportado])

  if (!soportado)
    return (
      <div className="visor visor-vacio" style={{ height: alto }}>
        <p>Este tipo de archivo no se puede previsualizar en el navegador.</p>
        <p className="texto-suave">Descárguelo para abrirlo con su aplicación.</p>
      </div>
    )
  if (error) return <div className="visor visor-vacio" style={{ height: alto }}><p className="texto-error">{error}</p></div>
  if (texto !== undefined) return <pre className="visor visor-texto" style={{ height: alto }}>{texto}</pre>
  if (!url) return <div className="visor visor-vacio" style={{ height: alto }}><Cargando texto="Abriendo documento…" /></div>
  if (mime.startsWith('image/'))
    return <div className="visor visor-imagen" style={{ height: alto }}><img src={url} alt="Documento" /></div>
  return <iframe className="visor" style={{ height: alto }} src={url} title="Visor de documento" />
}
