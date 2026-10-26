import { build } from 'esbuild'
import { execFileSync } from 'node:child_process'
import { copyFileSync, writeFileSync, mkdirSync } from 'node:fs'

mkdirSync('dist', { recursive: true })
await build({
  entryPoints: ['src/main.mjs'],
  bundle: true,
  platform: 'node',
  target: 'node24',
  format: 'cjs',
  outfile: 'dist/sidecar.cjs',
  external: ['sharp', 'jimp', 'link-preview-js', 'audio-decode', 'qrcode-terminal', 'ffmpeg-static', '@ffmpeg-installer/ffmpeg'],
  alias: {
    bufferutil: './stubs/bufferutil.cjs',
    'utf-8-validate': './stubs/utf-8-validate.cjs',
    'zlib-sync': './stubs/empty.cjs',
    erlpack: './stubs/empty.cjs',
    '@discordjs/opus': './stubs/empty.cjs',
    'sodium-native': './stubs/empty.cjs',
    'libsodium-wrappers': './stubs/empty.cjs',
    opusscript: './stubs/empty.cjs',
  },
  logLevel: 'warning',
})
writeFileSync('dist/sea-config.json', JSON.stringify({ main: 'dist/sidecar.cjs', output: 'dist/sea-prep.blob', disableExperimentalSEAWarning: true, useCodeCache: false }))
execFileSync(process.execPath, ['--experimental-sea-config', 'dist/sea-config.json'], { stdio: 'inherit' })
copyFileSync(process.execPath, 'dist/wa-sidecar.exe')
execFileSync('npx', ['--yes', 'postject', 'dist/wa-sidecar.exe', 'NODE_SEA_BLOB', 'dist/sea-prep.blob', '--sentinel-fuse', 'NODE_SEA_FUSE_fce680ab2cc467b6e072b8b5df1996b2'], { stdio: 'inherit', shell: true })
console.log('dist/wa-sidecar.exe')
