/**
 * Enhanced Part-Aware Form Persistence System
 * With improved handling of multiple entries and consistent UI updates
 */
(function () {
    // Configuration
    const saveInterval = 3000;          // Save every 3 seconds
    const expiryHours = 24;             // Keep saved data for 24 hours
    const debugMode = true;             // Set to false in production
    const forceLoginFlag = 'isForceLogin';
    const lastPageKey = 'lastActivePage';
    const lastSaveTimeKey = 'lastSaveTime';
    const reviewEntriesKey = 'scrapEntries';
    const partInfoKey = 'lastScrapPartInfo';

    // Initialize when DOM is ready
    document.addEventListener('DOMContentLoaded', function () {
        // Log info if in debug mode
        if (debugMode) {
            console.log(`Enhanced part-aware form persistence system initialized on ${window.location.pathname}`);
        }

        // Store current page as last active
        storeCurrentPage();

        // Special handling for scrap entry page
        if (window.location.pathname.includes('/ScrapEntry')) {
            setupScrapEntryPersistence();
        }

        // Check for force login redirect - do this after setup
        setTimeout(checkForceLoginRedirect, 500);
    });

    /**
     * Store the current page URL to localStorage
     */
    function storeCurrentPage() {
        // Don't store the login page
        if (window.location.pathname.includes('/Account/Login')) {
            return;
        }

        localStorage.setItem(lastPageKey, window.location.pathname + window.location.search);
        if (debugMode) {
            console.log(`Stored current page: ${window.location.pathname + window.location.search}`);
        }
    }

    /**
     * Extract current part information from the page or URL
     */
    function extractCurrentPartInfo() {
        // Extract from URL parameters
        const urlParams = new URLSearchParams(window.location.search);

        // Get from URL parameters
        const partNumber = urlParams.get('partNumber');
        const stockUom = urlParams.get('stockUom');
        const materialCost = urlParams.get('materialCost');

        // If not in URL, try getting from page elements
        let partNumberFromPage = document.getElementById('partNumber')?.textContent?.trim();
        let departmentFromPage = document.getElementById('department')?.textContent?.trim();
        let lineFromPage = document.getElementById('line')?.textContent?.trim();
        let shiftFromPage = document.getElementById('shift')?.textContent?.trim();

        // Return the part info
        return {
            partNumber: partNumber || partNumberFromPage || null,
            stockUom: stockUom || document.getElementById('stockUom')?.value || null,
            materialCost: materialCost || document.getElementById('materialCost')?.value || null,
            department: departmentFromPage || null,
            line: lineFromPage || null,
            shift: shiftFromPage || null,
            timestamp: new Date().toISOString()
        };
    }

    /**
     * Store current part information
     */
    function storeCurrentPartInfo() {
        if (!window.location.pathname.includes('/ScrapEntry')) {
            return;
        }

        const partInfo = extractCurrentPartInfo();
        if (partInfo.partNumber) {
            localStorage.setItem(partInfoKey, JSON.stringify(partInfo));
            if (debugMode) {
                console.log(`Stored part info for: ${partInfo.partNumber}`);
            }
        }
    }

    /**
     * Check if this page load was from a force login redirect
     */
    function checkForceLoginRedirect() {
        const isForceLogin = sessionStorage.getItem(forceLoginFlag);

        if (isForceLogin === 'true') {
            if (debugMode) {
                console.log('Force login detected');
            }

            // Clear the flag immediately to prevent repeats
            sessionStorage.removeItem(forceLoginFlag);

            // Get the last active page path
            const lastActivePage = localStorage.getItem(lastPageKey);

            // If we're on the home page but were working on scrap entry
            if (isHomePage() && lastActivePage && lastActivePage.includes('/ScrapEntry')) {
                const savedPartInfo = localStorage.getItem(partInfoKey);

                if (savedPartInfo) {
                    try {
                        const partInfo = JSON.parse(savedPartInfo);
                        const message = `You were working on part ${partInfo.partNumber} before your session was interrupted. Would you like to return to it?`;

                        if (confirm(message)) {
                            // Either go to ScanPart page or direct to ScrapEntry with params
                            if (partInfo.partNumber && partInfo.stockUom && partInfo.materialCost) {
                                window.location.href = `/Operator/ScrapEntry?partNumber=${encodeURIComponent(partInfo.partNumber)}&stockUom=${encodeURIComponent(partInfo.stockUom)}&materialCost=${encodeURIComponent(partInfo.materialCost)}`;
                            } else {
                                window.location.href = '/Operator/ScanPart';
                            }
                            return;
                        }
                    } catch (e) {
                        console.error('Error parsing saved part info', e);
                    }
                } else if (confirm('You were working on the Scrap Entry page before your session was interrupted. Would you like to return?')) {
                    window.location.href = '/Operator/ScanPart';
                    return;
                }
            }

            // Special handling for ScrapEntry page with mismatched parts
            if (window.location.pathname.includes('/ScrapEntry')) {
                handleScrapEntryPartMismatch();
            }
        }
    }

    /**
     * Handle part mismatches on ScrapEntry page
     */
    function handleScrapEntryPartMismatch() {
        const currentPartInfo = extractCurrentPartInfo();
        const savedEntries = localStorage.getItem(reviewEntriesKey);

        if (savedEntries && currentPartInfo.partNumber) {
            try {
                const entries = JSON.parse(savedEntries);

                if (entries.length === 0) return;

                // Check if any entry has a different part number
                const differentParts = entries.filter(e => e.partNumber !== currentPartInfo.partNumber);

                if (differentParts.length > 0) {
                    // Group entries by part number
                    const partGroups = {};
                    entries.forEach(entry => {
                        if (!partGroups[entry.partNumber]) {
                            partGroups[entry.partNumber] = [];
                        }
                        partGroups[entry.partNumber].push(entry);
                    });

                    // Create a message showing all different parts
                    const partNumbers = Object.keys(partGroups);
                    let message;

                    if (partNumbers.length === 2) {
                        const otherPart = partNumbers.find(p => p !== currentPartInfo.partNumber);
                        message = `You have saved entries for part ${otherPart}, but you're now working on ${currentPartInfo.partNumber}. Would you like to convert ALL entries to the current part?`;
                    } else {
                        const otherParts = partNumbers.filter(p => p !== currentPartInfo.partNumber).join(', ');
                        message = `You have saved entries for multiple parts (${otherParts}), but you're now working on ${currentPartInfo.partNumber}. Would you like to convert ALL entries to the current part?`;
                    }

                    if (confirm(message)) {
                        // Update ALL entries with current part info
                        entries.forEach(entry => {
                            entry.partNumber = currentPartInfo.partNumber;
                            if (currentPartInfo.department) entry.department = currentPartInfo.department;
                            if (currentPartInfo.line) entry.line = currentPartInfo.line;
                            if (currentPartInfo.shift) entry.shift = currentPartInfo.shift;
                        });

                        // Save back to session and localStorage
                        sessionStorage.setItem(reviewEntriesKey, JSON.stringify(entries));
                        localStorage.setItem(reviewEntriesKey, JSON.stringify(entries));

                        // Force a UI update
                        updateScrapEntryUI();

                        if (debugMode) {
                            console.log(`Converted ${entries.length} entries from multiple parts to ${currentPartInfo.partNumber}`);
                        }
                    } else {
                        // User chose not to convert - ask if they want to clear
                        if (confirm("Would you like to clear the previous entries and start fresh with the current part?")) {
                            // Clear entries in both storages
                            sessionStorage.removeItem(reviewEntriesKey);
                            localStorage.removeItem(reviewEntriesKey);

                            // Force UI update
                            updateScrapEntryUI();

                            if (debugMode) {
                                console.log('Cleared previous entries per user request');
                            }
                        }
                    }
                }
            } catch (e) {
                console.error('Error handling part mismatch', e);
            }
        }
    }

    /**
     * Force update of the ScrapEntry UI
     */
    function updateScrapEntryUI() {
        // Call the page's updateReviewTable function if available
        if (typeof window.updateReviewTable === 'function') {
            window.updateReviewTable();
            if (debugMode) console.log('Called updateReviewTable to refresh UI');
        } else {
            // If updateReviewTable isn't available, try loadEntriesFromSession
            if (typeof window.loadEntriesFromSession === 'function') {
                window.loadEntriesFromSession();
                if (debugMode) console.log('Called loadEntriesFromSession to refresh UI');
            } else {
                // Last resort - reload the page
                if (debugMode) console.log('No update functions available, preparing to reload page');
                setTimeout(function () {
                    location.reload();
                }, 100);
            }
        }
    }

    /**
     * Check if current page is a dashboard/home page
     */
    function isHomePage() {
        const path = window.location.pathname;
        return path === '/' ||
            path === '/Home' ||
            path === '/Home/Index' ||
            path === '/Operator' ||
            path === '/Operator/Index' ||
            path === '/ScrapApproval/Dashboard';
    }

    /**
     * Special handling for scrap entry page
     */
    function setupScrapEntryPersistence() {
        if (debugMode) console.log('Setting up enhanced scrap entry persistence');

        // Store the current part info
        storeCurrentPartInfo();

        // If force login occurred, check for saved entries
        if (sessionStorage.getItem(forceLoginFlag) === 'true') {
            // Check for saved entries
            const savedEntries = localStorage.getItem(reviewEntriesKey);

            if (savedEntries) {
                if (debugMode) console.log('Found saved scrap entries to restore');
                // Copy to sessionStorage where the page expects them
                sessionStorage.setItem(reviewEntriesKey, savedEntries);
            }
        }

        // Patch the page's original JS functions to handle part changes
        patchScrapEntryFunctions();

        // Use MutationObserver to watch for entry changes
        setupEntryObserver();
    }

    /**
     * Set up an observer to watch for entry changes
     */
    function setupEntryObserver() {
        // Listen for changes to the review table
        const observer = new MutationObserver(function (mutations) {
            // When the review table is updated, save entries to localStorage too
            const entries = sessionStorage.getItem(reviewEntriesKey);
            if (entries) {
                localStorage.setItem(reviewEntriesKey, entries);
                if (debugMode) console.log('Saved scrap entries to localStorage');
            }
        });

        // Try to find the review table element
        const reviewTable = document.getElementById('reviewTableBody');

        if (reviewTable) {
            observer.observe(reviewTable, {
                childList: true,
                subtree: true
            });
        } else if (debugMode) {
            console.log('Review table not found, observer not attached');
        }
    }

    /**
     * Patch the original page functions to handle part changes
     */
    function patchScrapEntryFunctions() {
        // Ensure we're on the ScrapEntry page
        if (!window.location.pathname.includes('/ScrapEntry')) return;

        // Wait for page JS to fully load
        setTimeout(function () {
            // Patch the createEntryObject function to ensure it gets current part info
            if (typeof window.createEntryObject === 'function') {
                const originalCreateEntry = window.createEntryObject;
                window.createEntryObject = function () {
                    const entry = originalCreateEntry.apply(this, arguments);

                    // Make sure the entry has the current part info
                    if (entry) {
                        const currentPartInfo = extractCurrentPartInfo();
                        if (currentPartInfo.partNumber && entry.partNumber !== currentPartInfo.partNumber) {
                            if (debugMode) console.log(`Updating entry part from ${entry.partNumber} to ${currentPartInfo.partNumber}`);
                            entry.partNumber = currentPartInfo.partNumber;
                            if (currentPartInfo.department) entry.department = currentPartInfo.department;
                            if (currentPartInfo.line) entry.line = currentPartInfo.line;
                            if (currentPartInfo.shift) entry.shift = currentPartInfo.shift;
                        }
                    }

                    return entry;
                };
                if (debugMode) console.log('Patched createEntryObject function');
            }

            // Patch addToReview to ensure entries have current part info
            if (typeof window.addToReview === 'function') {
                const originalAddToReview = window.addToReview;
                window.addToReview = function () {
                    // Before adding, ensure we have the current part's info
                    storeCurrentPartInfo();

                    // Call original function
                    originalAddToReview.apply(this, arguments);

                    // After adding, ensure entry was added with correct part info
                    setTimeout(function () {
                        try {
                            const entries = JSON.parse(sessionStorage.getItem(reviewEntriesKey) || '[]');
                            if (entries.length > 0) {
                                const currentPartInfo = extractCurrentPartInfo();
                                let updated = false;

                                // Check the last added entry
                                const lastEntry = entries[entries.length - 1];
                                if (lastEntry && lastEntry.partNumber !== currentPartInfo.partNumber) {
                                    lastEntry.partNumber = currentPartInfo.partNumber;
                                    if (currentPartInfo.department) lastEntry.department = currentPartInfo.department;
                                    if (currentPartInfo.line) lastEntry.line = currentPartInfo.line;
                                    if (currentPartInfo.shift) lastEntry.shift = currentPartInfo.shift;
                                    updated = true;
                                }

                                if (updated) {
                                    // Save back and update UI
                                    sessionStorage.setItem(reviewEntriesKey, JSON.stringify(entries));
                                    localStorage.setItem(reviewEntriesKey, JSON.stringify(entries));
                                    updateScrapEntryUI();
                                    if (debugMode) console.log('Updated last added entry with current part info');
                                }
                            }
                        } catch (e) {
                            console.error('Error checking last added entry', e);
                        }
                    }, 100);
                };
                if (debugMode) console.log('Patched addToReview function');
            }

            // Patch updateReviewTable to ensure it shows current part info
            if (typeof window.updateReviewTable === 'function') {
                const originalUpdateTable = window.updateReviewTable;
                window.updateReviewTable = function () {
                    // Before updating, check if entries have correct part info
                    try {
                        const entries = JSON.parse(sessionStorage.getItem(reviewEntriesKey) || '[]');
                        if (entries.length > 0) {
                            const currentPartInfo = extractCurrentPartInfo();
                            let updated = false;

                            // Check all entries
                            entries.forEach(entry => {
                                if (entry.partNumber !== currentPartInfo.partNumber) {
                                    entry.partNumber = currentPartInfo.partNumber;
                                    if (currentPartInfo.department) entry.department = currentPartInfo.department;
                                    if (currentPartInfo.line) entry.line = currentPartInfo.line;
                                    if (currentPartInfo.shift) entry.shift = currentPartInfo.shift;
                                    updated = true;
                                }
                            });

                            if (updated) {
                                // Save back
                                sessionStorage.setItem(reviewEntriesKey, JSON.stringify(entries));
                                localStorage.setItem(reviewEntriesKey, JSON.stringify(entries));
                                if (debugMode) console.log('Updated all entries with current part info');
                            }
                        }
                    } catch (e) {
                        console.error('Error checking entries before update', e);
                    }

                    // Call original function
                    return originalUpdateTable.apply(this, arguments);
                };
                if (debugMode) console.log('Patched updateReviewTable function');
            }

            // Patch handleSubmitAll to clear storage after successful submission
            if (typeof window.handleSubmitAll === 'function') {
                const originalSubmitAll = window.handleSubmitAll;
                window.handleSubmitAll = function () {
                    // Call original
                    const result = originalSubmitAll.apply(this, arguments);

                    // Clear storage after successful submission
                    setTimeout(function () {
                        // Check if entries were cleared (submission successful)
                        const reviewTableBody = document.getElementById('reviewTableBody');
                        if (reviewTableBody && (!reviewTableBody.children.length || reviewTableBody.children.length === 0)) {
                            localStorage.removeItem(reviewEntriesKey);
                            sessionStorage.removeItem(reviewEntriesKey);
                            if (debugMode) console.log('Cleared entries after submission');
                        }
                    }, 5000);

                    return result;
                };
                if (debugMode) console.log('Patched handleSubmitAll function');
            }

            // Patch loadEntriesFromSession to handle part number mismatches
            if (typeof window.loadEntriesFromSession === 'function') {
                const originalLoadEntries = window.loadEntriesFromSession;
                window.loadEntriesFromSession = function () {
                    try {
                        const currentPartInfo = extractCurrentPartInfo();
                        const savedEntries = JSON.parse(sessionStorage.getItem(reviewEntriesKey) || '[]');

                        if (savedEntries.length > 0 && currentPartInfo.partNumber) {
                            // Check for mismatched parts
                            const differentParts = savedEntries.filter(e => e.partNumber !== currentPartInfo.partNumber);

                            if (differentParts.length > 0) {
                                // Update all entries with current part info
                                savedEntries.forEach(entry => {
                                    if (entry.partNumber !== currentPartInfo.partNumber) {
                                        entry.partNumber = currentPartInfo.partNumber;
                                        if (currentPartInfo.department) entry.department = currentPartInfo.department;
                                        if (currentPartInfo.line) entry.line = currentPartInfo.line;
                                        if (currentPartInfo.shift) entry.shift = currentPartInfo.shift;
                                    }
                                });

                                // Save back
                                sessionStorage.setItem(reviewEntriesKey, JSON.stringify(savedEntries));
                                localStorage.setItem(reviewEntriesKey, JSON.stringify(savedEntries));

                                if (debugMode) {
                                    console.log(`Updated ${differentParts.length} entries with current part info`);
                                }
                            }
                        }
                    } catch (e) {
                        console.error('Error in enhanced loadEntriesFromSession', e);
                    }

                    // Call original
                    return originalLoadEntries.apply(this, arguments);
                };
                if (debugMode) console.log('Patched loadEntriesFromSession function');
            }
        }, 500);
    }
})();