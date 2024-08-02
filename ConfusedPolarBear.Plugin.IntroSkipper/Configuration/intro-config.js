// first and second episodes to fingerprint & compare
let lhs = [];
let rhs = [];

// fingerprint point comparison & miminum similarity threshold (at most 6 bits out of 32 can be different)
let fprDiffs = [];
let fprDiffMinimum = (1 - 6 / 32) * 100;

// seasons grouped by show
let shows = {};

// settings elements
let visualizer = document.querySelector("details#visualizer");
let support = document.querySelector("details#support");
let btnEraseIntroTimestamps = document.querySelector("button#btnEraseIntroTimestamps");
let btnEraseCreditTimestamps = document.querySelector("button#btnEraseCreditTimestamps");

// all plugin configuration fields that can be get or set with .value (i.e. strings or numbers).
let configurationFields = [
    // analysis
    "MaxParallelism",
    "SelectedLibraries",
    "AnalysisPercent",
    "AnalysisLengthLimit",
    "MinimumIntroDuration",
    "MaximumIntroDuration",
    "MinimumCreditsDuration",
    "MaximumCreditsDuration",
    "EdlAction",
    "ProcessPriority",
    "ProcessThreads",
    // playback
    "ShowPromptAdjustment",
    "HidePromptAdjustment",
    "SecondsOfIntroToPlay",
    // internals
    "SilenceDetectionMaximumNoise",
    "SilenceDetectionMinimumDuration",
    // UI customization
    "SkipButtonIntroText",
    "SkipButtonEndCreditsText",
    "AutoSkipNotificationText",
    "AutoSkipCreditsNotificationText"
]

let booleanConfigurationFields = [
    "AutoDetectIntros",
    "AutoDetectCredits",
    "AnalyzeSeasonZero",
    "RegenerateEdlFiles",
    "UseChromaprint",
    "CacheFingerprints",
    "AutoSkip",
    "AutoSkipCredits",
    "SkipFirstEpisode",
    "PersistSkipButton",
    "SkipButtonVisible"
]

// visualizer elements
let canvas = document.querySelector("canvas#troubleshooter");
let selectShow = document.querySelector("select#troubleshooterShow");
let selectSeason = document.querySelector("select#troubleshooterSeason");
let selectEpisode1 = document.querySelector("select#troubleshooterEpisode1");
let selectEpisode2 = document.querySelector("select#troubleshooterEpisode2");
let txtOffset = document.querySelector("input#offset");
let txtSuggested = document.querySelector("span#suggestedShifts");
let btnSeasonEraseTimestamps = document.querySelector("button#btnEraseSeasonTimestamps");
let eraseSeasonContainer = document.getElementById("eraseSeasonContainer");
let timestampError = document.querySelector("textarea#timestampError");
let timestampErrorDiv = document.querySelector("div#timestampErrorDiv");
let timestampEditor = document.querySelector("#timestampEditor");
let btnUpdateTimestamps = document.querySelector("button#btnUpdateTimestamps");
let timeContainer = document.querySelector("span#timestampContainer");

let windowHashInterval = 0;

let autoSkip = document.querySelector("input#AutoSkip");
let skipFirstEpisode = document.querySelector("div#divSkipFirstEpisode");
let autoSkipNotificationText = document.querySelector("div#divAutoSkipNotificationText");
let autoSkipCredits = document.querySelector("input#AutoSkipCredits");
let autoSkipCreditsNotificationText = document.querySelector("div#divAutoSkipCreditsNotificationText");

async function autoSkipChanged() {
    if (autoSkip.checked) {
        skipFirstEpisode.style.display = 'unset';
        autoSkipNotificationText.style.display = 'unset';
    } else {
        skipFirstEpisode.style.display = 'none';
        autoSkipNotificationText.style.display = 'none';
    }
}

autoSkip.addEventListener("change", autoSkipChanged);

async function autoSkipCreditsChanged() {
    if (autoSkipCredits.checked) {
        autoSkipCreditsNotificationText.style.display = 'unset';
    } else {
        autoSkipCreditsNotificationText.style.display = 'none';
    }
}

autoSkipCredits.addEventListener("change", autoSkipCreditsChanged);

let persistSkip = document.querySelector("input#PersistSkipButton");
let showAdjustment = document.querySelector("div#divShowPromptAdjustment");
let hideAdjustment = document.querySelector("div#divHidePromptAdjustment");

// prevent setting unavailable options
async function persistSkipChanged() {
    if (persistSkip.checked) {
        showAdjustment.style.display = 'none';
        hideAdjustment.style.display = 'none';
    } else {
        showAdjustment.style.display = 'unset';
        hideAdjustment.style.display = 'unset';
    }
}

persistSkip.addEventListener("change", persistSkipChanged);

// when the fingerprint visualizer opens, populate show names
async function visualizerToggled() {
    if (!visualizer.open) {
        return;
    }

    // ensure the series select is empty
    while (selectShow.options.length > 0) {
        selectShow.remove(0);
    }

    Dashboard.showLoadingMsg();

    shows = await getJson("Intros/Shows");

    let sorted = [];
    for (let series in shows) { sorted.push(series); }
    sorted.sort();

    for (let show of sorted) {
        addItem(selectShow, show, show);
    }

    selectShow.value = "";

    Dashboard.hideLoadingMsg();
}

// fetch the support bundle whenever the detail section is opened.
async function supportToggled() {
    if (!support.open) {
        return;
    }

    // Fetch the support bundle
    const bundle = await fetchWithAuth("IntroSkipper/SupportBundle", "GET", null);
    const bundleText = await bundle.text();

    // Display it to the user and select all
    const ta = document.querySelector("textarea#supportBundle");
    ta.value = bundleText;
    ta.focus();
    ta.setSelectionRange(0, ta.value.length);

    // Attempt to copy it to the clipboard automatically, falling back
    // to prompting the user to press Ctrl + C.
    try {
        navigator.clipboard.writeText(bundleText);
        Dashboard.alert("Support bundle copied to clipboard");
    } catch {
        Dashboard.alert("Press Ctrl+C to copy support bundle");
    }
}

// show changed, populate seasons
async function showChanged() {
    clearSelect(selectSeason);
    eraseSeasonContainer.style.display = "none";
    clearSelect(selectEpisode1);
    clearSelect(selectEpisode2);

    // add all seasons from this show to the season select
    for (let season of shows[selectShow.value]) {
        addItem(selectSeason, season, season);
    }

    selectSeason.value = "";
}

// season changed, reload all episodes
async function seasonChanged() {
    const url = "Intros/Show/" + encodeURI(selectShow.value) + "/" + selectSeason.value;
    const episodes = await getJson(url);

    clearSelect(selectEpisode1);
    clearSelect(selectEpisode2);
    eraseSeasonContainer.style.display = "unset";

    let i = 1;
    for (let episode of episodes) {
        const strI = i.toLocaleString("en", { minimumIntegerDigits: 2, maximumFractionDigits: 0 });
        addItem(selectEpisode1, strI + ": " + episode.Name, episode.Id);
        addItem(selectEpisode2, strI + ": " + episode.Name, episode.Id);
        i++;
    }

    setTimeout(() => {
        selectEpisode1.selectedIndex = 0;
        selectEpisode2.selectedIndex = 1;
        episodeChanged();
    }, 100);
}

// episode changed, get fingerprints & calculate diff
async function episodeChanged() {
    if (!selectEpisode1.value || !selectEpisode2.value) {
        return;
    }

    Dashboard.showLoadingMsg();

    timestampError.value = "";
    canvas.style.display = "none";

    lhs = await getJson("Intros/Episode/" + selectEpisode1.value + "/Chromaprint");
    if (lhs === undefined) {
        timestampError.value += "Error: " + selectEpisode1.value + " fingerprints failed!\n";
    } else if (lhs === null) {
        timestampError.value += "Error: " + selectEpisode1.value + " fingerprints missing!\n";
    }

    rhs = await getJson("Intros/Episode/" + selectEpisode2.value + "/Chromaprint");
    if (rhs === undefined) {
        timestampError.value += "Error: " + selectEpisode2.value + " fingerprints failed!";
    } else if (rhs === null) {
        timestampError.value += "Error: " + selectEpisode2.value + " fingerprints missing!\n";
    }

    if (timestampError.value == "") {
        timestampErrorDiv.style.display = "none";
    } else {
        timestampErrorDiv.style.display = "unset";
    }

    Dashboard.hideLoadingMsg();

    txtOffset.value = "0";
    refreshBounds();
    renderTroubleshooter();
    findExactMatches();
    updateTimestampEditor();
}

// updates the timestamp editor
async function updateTimestampEditor() {
    // Get the title and ID of the left and right episodes
    const leftEpisode = selectEpisode1.options[selectEpisode1.selectedIndex];
    const rightEpisode = selectEpisode2.options[selectEpisode2.selectedIndex];
    // Try to get the timestamps of each intro, falling back a default value of zero if no intro was found
    const leftEpisodeJson = await getJson("Episode/" + leftEpisode.value + "/Timestamps");
    const rightEpisodeJson = await getJson("Episode/" + rightEpisode.value + "/Timestamps");

    // Update the editor for the first and second episodes
    timestampEditor.style.display = "unset";
    document.querySelector("#editLeftEpisodeTitle").textContent = leftEpisode.text;
    document.querySelector("#editLeftIntroEpisodeStart").value = setTime(Math.round(leftEpisodeJson.Introduction.IntroStart));
    document.querySelector("#editLeftIntroEpisodeEnd").value = setTime(Math.round(leftEpisodeJson.Introduction.IntroEnd));
    document.querySelector("#editLeftCreditEpisodeStart").value = setTime(Math.round(leftEpisodeJson.Credits.IntroStart));
    document.querySelector("#editLeftCreditEpisodeEnd").value = setTime(Math.round(leftEpisodeJson.Credits.IntroEnd));

    document.querySelector("#editRightEpisodeTitle").textContent = rightEpisode.text;
    document.querySelector("#editRightIntroEpisodeStart").value = setTime(Math.round(rightEpisodeJson.Introduction.IntroStart));
    document.querySelector("#editRightIntroEpisodeEnd").value = setTime(Math.round(rightEpisodeJson.Introduction.IntroEnd));
    document.querySelector("#editRightCreditEpisodeStart").value = setTime(Math.round(rightEpisodeJson.Credits.IntroStart));
    document.querySelector("#editRightCreditEpisodeEnd").value = setTime(Math.round(rightEpisodeJson.Credits.IntroEnd));

}

// adds an item to a dropdown
function addItem(select, text, value) {
    let item = new Option(text, value);
    select.add(item);
}

// clear a select of items
function clearSelect(select) {
    timestampError.value = "";
    timestampErrorDiv.style.display = "none";
    timestampEditor.style.display = "none";
    timeContainer.style.display = "none";
    canvas.style.display = "none";
    let i, L = select.options.length - 1;
    for (i = L; i >= 0; i--) {
        select.remove(i);
    }
}

// make an authenticated GET to the server and parse the response as JSON
async function getJson(url) {
    return await fetchWithAuth(url, "GET")
        .then(r => {
            if (r.ok) {
                return r.json();
            } else {
                return null;
            }
        })
        .catch(err => {
            console.debug(err);
        });
}

// make an authenticated fetch to the server
async function fetchWithAuth(url, method, body) {
    url = ApiClient.serverAddress() + "/" + url;

    const reqInit = {
        method: method,
        headers: {
            "Authorization": "MediaBrowser Token=" + ApiClient.accessToken()
        },
        body: body,
    };

    if (method === "POST") {
        reqInit.headers["Content-Type"] = "application/json";
    }

    return await fetch(url, reqInit);
}

// key pressed
function keyDown(e) {
    let episodeDelta = 0;
    let offsetDelta = 0;

    switch (e.key) {
        case "ArrowDown":
            if (timestampError.value != "") {
                // if the control key is pressed, shift LHS by 10s. Otherwise, shift by 1.
                offsetDelta = e.ctrlKey ? 10 / 0.1238 : 1;
            }
            break;

        case "ArrowUp":
            if (timestampError.value != "") {
                offsetDelta = e.ctrlKey ? -10 / 0.1238 : -1;
            }
            break;

        case "ArrowRight":
            episodeDelta = 2;
            break;

        case "ArrowLeft":
            episodeDelta = -2;
            break;

        default:
            return;
    }

    if (offsetDelta != 0) {
        txtOffset.value = Number(txtOffset.value) + Math.floor(offsetDelta);
    }

    if (episodeDelta != 0) {
        // calculate the number of episodes remaining in the LHS and RHS episode pickers
        const lhsRemaining = selectEpisode1.selectedIndex;
        const rhsRemaining = selectEpisode2.length - selectEpisode2.selectedIndex - 1;

        // if we're moving forward and the right episode picker is close to the end, don't move.
        if (episodeDelta > 0 && rhsRemaining <= 1) {
            return;
        } else if (episodeDelta < 0 && lhsRemaining <= 1) {
            return;
        }

        selectEpisode1.selectedIndex += episodeDelta;
        selectEpisode2.selectedIndex += episodeDelta;
        episodeChanged();
    }

    renderTroubleshooter();
    e.preventDefault();
}

// check that the user is still on the configuration page
function checkWindowHash() {
    const h = location.hash;
    if (h === "#!/configurationpage?name=Intro%20Skipper" || h.includes("#!/dialog")) {
        return;
    }

    console.debug("navigated away from intro skipper configuration page");
    document.removeEventListener("keydown", keyDown);
    clearInterval(windowHashInterval);
}

// converts seconds to a readable timestamp (i.e. 127 becomes "02:07").
function secondsToString(seconds) {
    return new Date(seconds * 1000).toISOString().slice(14, 19);
}

// erase all intro/credits timestamps
function eraseTimestamps(mode) {
    const lower = mode.toLocaleLowerCase();
    const title = "Confirm timestamp erasure";
    const body = "Are you sure you want to erase all previously discovered " +
        mode.toLocaleLowerCase() +
        " timestamps?";
    const eraseCacheChecked = document.getElementById("eraseModeCacheCheckbox").checked;

    Dashboard.confirm(
        body,
        title,
        (result) => {
            if (!result) {
                return;
            }

            fetchWithAuth("Intros/EraseTimestamps?mode=" + mode + "&eraseCache=" + eraseCacheChecked, "POST", null);

            Dashboard.alert(mode + " timestamps erased");
        });
}

document.querySelector('#TemplateConfigPage')
    .addEventListener('pageshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration("c83d86bb-a1e0-4c35-a113-e2101cf4ee6b").then(function (config) {
            for (const field of configurationFields) {
                document.querySelector("#" + field).value = config[field];
            }

            for (const field of booleanConfigurationFields) {
                document.querySelector("#" + field).checked = config[field];
            }

            autoSkipChanged();
            autoSkipCreditsChanged();
            persistSkipChanged();

            Dashboard.hideLoadingMsg();
        });
    });

document.querySelector('#FingerprintConfigForm')
    .addEventListener('submit', function (e) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration("c83d86bb-a1e0-4c35-a113-e2101cf4ee6b").then(function (config) {
            for (const field of configurationFields) {
                config[field] = document.querySelector("#" + field).value;
            }

            for (const field of booleanConfigurationFields) {
                config[field] = document.querySelector("#" + field).checked;
            }

            ApiClient.updatePluginConfiguration("c83d86bb-a1e0-4c35-a113-e2101cf4ee6b", config)
                .then(function (result) {
                    Dashboard.processPluginConfigurationUpdateResult(result);
                });
        });

        e.preventDefault();
        return false;
    });

visualizer.addEventListener("toggle", visualizerToggled);
support.addEventListener("toggle", supportToggled);
txtOffset.addEventListener("change", renderTroubleshooter);
selectShow.addEventListener("change", showChanged);
selectSeason.addEventListener("change", seasonChanged);
selectEpisode1.addEventListener("change", episodeChanged);
selectEpisode2.addEventListener("change", episodeChanged);
btnEraseIntroTimestamps.addEventListener("click", (e) => {
    eraseTimestamps("Introduction");
    e.preventDefault();
});
btnEraseCreditTimestamps.addEventListener("click", (e) => {
    eraseTimestamps("Credits");
    e.preventDefault();
});
btnSeasonEraseTimestamps.addEventListener("click", () => {
    Dashboard.confirm(
        "Are you sure you want to erase all timestamps for this season?",
        "Confirm timestamp erasure",
        (result) => {
            if (!result) {
                return;
            }

            const show = selectShow.value;
            const season = selectSeason.value;
            const eraseCacheChecked = document.getElementById("eraseSeasonCacheCheckbox").checked;

            const url = "Intros/Show/" + encodeURIComponent(show) + "/" + encodeURIComponent(season);
            fetchWithAuth(url + "?eraseCache=" + eraseCacheChecked, "DELETE", null);

            Dashboard.alert("Erased timestamps for " + season + " of " + show);
            document.getElementById("eraseSeasonCacheCheckbox").checked = false;
        }
    );
});
btnUpdateTimestamps.addEventListener("click", () => {
    const lhsId = selectEpisode1.options[selectEpisode1.selectedIndex].value;
    const newLhs = {
        Introduction: {
            IntroStart: getTimeInSeconds(document.getElementById('editLeftIntroEpisodeStart').value),
            IntroEnd: getTimeInSeconds(document.getElementById('editLeftIntroEpisodeEnd').value)
        },
        Credits: {
            IntroStart: getTimeInSeconds(document.getElementById('editLeftCreditEpisodeStart').value),
            IntroEnd: getTimeInSeconds(document.getElementById('editLeftCreditEpisodeEnd').value)
        }
    };

    const rhsId = selectEpisode2.options[selectEpisode2.selectedIndex].value;
    const newRhs = {
        Introduction: {
            IntroStart: getTimeInSeconds(document.getElementById('editRightIntroEpisodeStart').value),
            IntroEnd: getTimeInSeconds(document.getElementById('editRightIntroEpisodeEnd').value)
        },
        Credits: {
            IntroStart: getTimeInSeconds(document.getElementById('editRightCreditEpisodeStart').value),
            IntroEnd: getTimeInSeconds(document.getElementById('editRightCreditEpisodeEnd').value)
        }
    };
    fetchWithAuth("Episode/" + lhsId + "/Timestamps", "POST", JSON.stringify(newLhs));
    fetchWithAuth("Episode/" + rhsId + "/Timestamps", "POST", JSON.stringify(newRhs));

    Dashboard.alert("New introduction timestamps saved");
});
document.addEventListener("keydown", keyDown);
windowHashInterval = setInterval(checkWindowHash, 2500);

canvas.addEventListener("mousemove", (e) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const y = e.clientY - rect.top;
    const shift = Number(txtOffset.value);

    let lTime, rTime, diffPos;
    if (shift < 0) {
        lTime = y * 0.1238;
        rTime = (y + shift) * 0.1238;
        diffPos = y + shift;
    } else {
        lTime = (y - shift) * 0.1238;
        rTime = y * 0.1238;
        diffPos = y - shift;
    }

    const diff = fprDiffs[Math.floor(diffPos)];

    if (!diff) {
        timeContainer.style.display = "none";
        return;
    } else {
        timeContainer.style.display = "unset";
    }

    const times = document.querySelector("span#timestamps");

    // LHS timestamp, RHS timestamp, percent similarity
    times.textContent =
        secondsToString(lTime) + ", " +
        secondsToString(rTime) + ", " +
        Math.round(diff) + "%";

    timeContainer.style.position = "relative";
    timeContainer.style.left = "25px";
    timeContainer.style.top = (-1 * rect.height + y).toString() + "px";
});

function setTime(seconds) {
    // Calculate hours, minutes, and remaining seconds
    let hours = Math.floor(seconds / 3600);
    let minutes = Math.floor((seconds % 3600) / 60);
    let remainingSeconds = seconds % 60;

    // Format as HH:MM:SS
    let formattedTime =
        String(hours).padStart(2, '0') + ':' +
        String(minutes).padStart(2, '0') + ':' +
        String(remainingSeconds).padStart(2, '0');

    // Set the value of the time input
    return formattedTime;
}

function getTimeInSeconds(time) {
    let [hours, minutes, seconds] = time.split(':').map(Number);
    return (hours * 3600) + (minutes * 60) + seconds;
}