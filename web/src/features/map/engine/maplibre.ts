import 'maplibre-gl/dist/maplibre-gl.css'
import maplibregl from 'maplibre-gl'
import { Protocol } from 'pmtiles'

let registered = false

/** MapLibre with its CSS and the pmtiles:// protocol (for the offline base map); loaded only when a map opens. */
export function loadMapLibre(): typeof maplibregl {
  if (!registered) {
    maplibregl.addProtocol('pmtiles', new Protocol().tile)
    registered = true
  }
  return maplibregl
}

export default maplibregl
