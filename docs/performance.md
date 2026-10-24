<script setup>
import { withBase } from 'vitepress'
</script>

# Speed budgets

Every night the budget tests run against the controller, agent and client library; the numbers and the trend per measurement are kept on the `perf-results` branch and shown below. How the tests and the limits work is described under [Testing](testing#budgets).

<!-- A plain link would be taken over by the site's router, which has no page of that name; target _blank leaves it to the browser. -->
<p><a :href="withBase('/performance-trend.html')" target="_blank" rel="noopener">Open the trend page in its own tab</a></p>

<iframe :src="withBase('/performance-trend.html')" title="Speed budgets: the last night and the trend per measurement" class="trend" loading="lazy"></iframe>
