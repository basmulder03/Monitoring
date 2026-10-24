<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue'

const props = defineProps<{ code: string }>()
const holder = ref<HTMLElement>()
const failed = ref(false)
let watcher: MutationObserver | undefined
let counter = 0

// Drawn in the browser, in the colours of the current theme, and again when the reader switches between light and dark.
async function draw() {
  try {
    const { default: mermaid } = await import('mermaid')
    mermaid.initialize({ startOnLoad: false, theme: document.documentElement.classList.contains('dark') ? 'dark' : 'default' })
    const { svg } = await mermaid.render(`mermaid-${Math.random().toString(36).slice(2)}-${counter++}`, decodeURIComponent(props.code))
    if (holder.value) holder.value.innerHTML = svg
  } catch {
    failed.value = true
  }
}

onMounted(() => {
  draw()
  watcher = new MutationObserver(draw)
  watcher.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] })
})
onUnmounted(() => watcher?.disconnect())
</script>

<template>
  <pre v-if="failed"><code>{{ decodeURIComponent(props.code) }}</code></pre>
  <div v-else ref="holder" class="mermaid-diagram" />
</template>

<style>
.mermaid-diagram { display: flex; justify-content: center; margin: 1rem 0; overflow-x: auto; }
</style>
