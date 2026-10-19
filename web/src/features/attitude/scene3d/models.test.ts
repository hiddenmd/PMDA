// @vitest-environment node
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { Box3, Vector3, type Object3D } from 'three'
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js'
import { describe, expect, it } from 'vitest'

async function load(name: string): Promise<Object3D> {
  const path = fileURLToPath(new URL(`../../../../public/models/${name}.glb`, import.meta.url))
  const bytes = readFileSync(path)
  const buffer = bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength)
  const gltf = await new GLTFLoader().parseAsync(buffer, '')
  return gltf.scene
}

const size = (o: Object3D) => new Box3().setFromObject(o).getSize(new Vector3())

describe('authored aircraft models', () => {
  it('jet: named parts, nose along −Z, about 16 m long and 10 m span', async () => {
    const jet = await load('jet')
    expect(jet.getObjectByName('Body')).toBeDefined()
    expect(jet.getObjectByName('Canopy')).toBeDefined()
    const s = size(jet)
    expect(s.z).toBeGreaterThan(14)
    expect(s.z).toBeLessThan(18)
    expect(s.x).toBeGreaterThan(8)
    expect(s.x).toBeLessThan(12)
    const box = new Box3().setFromObject(jet)
    expect(box.min.z).toBeLessThan(-8) // the nose is the far −Z end
  })

  it('helicopter: named rotors pivoting at their hubs', async () => {
    const heli = await load('helicopter')
    for (const name of ['Body', 'Canopy', 'MainRotor', 'TailRotor']) expect(heli.getObjectByName(name), name).toBeDefined()
    const main = heli.getObjectByName('MainRotor')!
    const blades = size(main)
    expect(blades.x).toBeGreaterThan(10) // four blades, about 12 m across
    expect(Math.abs(blades.x - blades.z)).toBeLessThan(0.5)
  })
})
