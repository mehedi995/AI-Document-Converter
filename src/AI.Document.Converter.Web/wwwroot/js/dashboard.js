// Refreshes the recent-conversions table while work is genuinely in flight.
//
// Polls only while something is running and stops as soon as everything has
// finished - a page left open overnight should not keep hitting the server.
// Every number shown comes from the server; nothing is animated or estimated
// on the client, because a bar that advances on a timer tells the user
// something nobody actually knows (SaaS section 10).
(function () {
    "use strict";

    var table = document.getElementById("jobs-table");
    if (!table) {
        return;
    }

    var statusUrl = table.getAttribute("data-status-url");
    var POLL_MS = 3000;
    var timer = null;

    function hasInFlightRows() {
        return table.querySelector('tr[data-in-flight="true"]') !== null;
    }

    function applyUpdate(jobs) {
        jobs.forEach(function (job) {
            var row = table.querySelector('tr[data-job-id="' + job.id + '"]');
            if (!row) {
                return;
            }

            row.setAttribute("data-in-flight", job.isInFlight ? "true" : "false");

            var progress = row.querySelector(".js-progress");
            if (progress) {
                progress.textContent = job.progressDescription;
            }
        });

        // A job that just finished needs the full outcome markup, which is
        // rendered server-side. Reloading once is simpler and less
        // error-prone than duplicating that badge logic here.
        if (!jobs.some(function (j) { return j.isInFlight; })) {
            window.location.reload();
        }
    }

    function poll() {
        if (!hasInFlightRows()) {
            window.clearInterval(timer);
            return;
        }

        fetch(statusUrl, { headers: { "Accept": "application/json" } })
            .then(function (response) {
                return response.ok ? response.json() : null;
            })
            .then(function (data) {
                if (data && data.jobs) {
                    applyUpdate(data.jobs);
                }
            })
            .catch(function () {
                // A failed poll is not worth surfacing: the page already shows
                // the last known state, and the next tick will try again.
            });
    }

    if (hasInFlightRows()) {
        timer = window.setInterval(poll, POLL_MS);
    }
})();
