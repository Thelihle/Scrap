// EnhancedReset.js - Much more aggressive reset solution
(function () {
    console.log("Installing enhanced reset functionality");

    // Find the existing reset button or create a new one
    let resetButton = document.querySelector('[data-reset-entries="true"]');
    if (!resetButton) {
        resetButton = document.createElement('button');
        resetButton.setAttribute('data-reset-entries', 'true');
        resetButton.textContent = '🧹 Reset Entries';
        resetButton.style.position = 'fixed';
        resetButton.style.bottom = '20px';
        resetButton.style.right = '20px';
        resetButton.style.zIndex = '9999';
        resetButton.style.padding = '8px 16px';
        resetButton.style.background = '#e74c3c';
        resetButton.style.color = 'white';
        resetButton.style.border = 'none';
        resetButton.style.borderRadius = '4px';
        resetButton.style.fontWeight = 'bold';
        resetButton.style.cursor = 'pointer';
        resetButton.style.boxShadow = '0 2px 5px rgba(0,0,0,0.2)';
        document.body.appendChild(resetButton);
    }

    // Remove any existing click handlers by cloning and replacing the button
    const newButton = resetButton.cloneNode(true);
    resetButton.parentNode.replaceChild(newButton, resetButton);
    resetButton = newButton;

    // Add our enhanced reset functionality
    resetButton.addEventListener('click', function () {
        if (confirm('WARNING: This will perform a COMPLETE RESET of all entries. Continue?')) {
            console.log('[RESET] Starting AGGRESSIVE entry reset');

            try {
                // 1. DIRECT DOM MANIPULATION - Force clear the UI first
                const reviewTableBody = document.getElementById('reviewTableBody');
                if (reviewTableBody) {
                    console.log('[RESET] Clearing review table DOM');
                    reviewTableBody.innerHTML = '<div class="no-entries">No entries added yet.</div>';
                }

                // Update total quantity display
                const totalQuantity = document.getElementById('totalQuantity');
                if (totalQuantity) {
                    totalQuantity.textContent = '0';
                }

                // 2. CLEAR GLOBAL STATE - Reset the state object completely
                if (typeof window.state !== 'undefined') {
                    console.log('[RESET] Resetting global state object');
                    if (window.state.reviewEntries) {
                        window.state.reviewEntries = [];
                    }
                }

                // 3. CLEAR ALL SESSION STORAGE - Not just specific keys
                console.log('[RESET] Clearing ALL sessionStorage');
                for (let i = 0; i < sessionStorage.length; i++) {
                    const key = sessionStorage.key(i);
                    console.log(`[RESET] Removing sessionStorage key: ${key}`);
                    sessionStorage.removeItem(key);
                }
                sessionStorage.clear(); // Backup clear method

                // 4. CLEAR ALL LOCAL STORAGE - Not just specific keys
                // First, save any keys we want to preserve (non-scrap related)
                const preserveKeys = ['theme', 'language', 'userPreferences'];
                const preservedData = {};

                preserveKeys.forEach(key => {
                    const value = localStorage.getItem(key);
                    if (value) {
                        preservedData[key] = value;
                    }
                });

                console.log('[RESET] Clearing localStorage');
                for (let i = 0; i < localStorage.length; i++) {
                    const key = localStorage.key(i);
                    if (!preserveKeys.includes(key)) {
                        console.log(`[RESET] Removing localStorage key: ${key}`);
                        localStorage.removeItem(key);
                    }
                }

                // Restore preserved keys
                Object.keys(preservedData).forEach(key => {
                    localStorage.setItem(key, preservedData[key]);
                });

                // 5. RESET WINDOW.NAME
                if (window.name) {
                    window.name = '';
                    console.log('[RESET] Cleared window.name');
                }

                // 6. RESET ANY CUSTOM STATE VARIABLES
                // Search for any variables that might contain "scrap" or "entries"
                for (const key in window) {
                    if (typeof window[key] === 'object' && window[key] !== null) {
                        if (key.toLowerCase().includes('scrap') || key.toLowerCase().includes('entries')) {
                            try {
                                window[key] = {};
                                console.log(`[RESET] Reset window.${key}`);
                            } catch (e) {
                                // Ignore errors for read-only properties
                            }
                        }
                    }
                }

                // 7. ADD A MARKER TO PREVENT AUTO-RESTORE
                localStorage.setItem('preventScrapRestore', 'true');
                sessionStorage.setItem('preventScrapRestore', 'true');

                // 8. FORCE COOKIE CLEANUP
                document.cookie.split(";").forEach(function (c) {
                    if (c.toLowerCase().includes('scrap') || c.toLowerCase().includes('entries')) {
                        document.cookie = c.replace(/^ +/, "").replace(/=.*/, "=;expires=" + new Date().toUTCString() + ";path=/");
                        console.log(`[RESET] Cleared cookie: ${c}`);
                    }
                });

                // 9. OVERRIDE PERSISTENCE FUNCTIONS
                if (typeof window.saveEntriesToSession === 'function') {
                    window.saveEntriesToSession = function () {
                        console.log('[RESET] Blocked saveEntriesToSession call');
                        return false;
                    };
                }

                if (typeof window.updateReviewTable === 'function') {
                    window.updateReviewTable = function () {
                        console.log('[RESET] Forced empty review table');
                        const reviewTable = document.getElementById('reviewTableBody');
                        if (reviewTable) {
                            reviewTable.innerHTML = '<div class="no-entries">No entries added yet.</div>';
                        }
                        return false;
                    };
                }

                // 10. SHOW SUCCESS MESSAGE WITH COUNTDOWN
                const messageDiv = document.createElement('div');
                messageDiv.style.position = 'fixed';
                messageDiv.style.top = '50%';
                messageDiv.style.left = '50%';
                messageDiv.style.transform = 'translate(-50%, -50%)';
                messageDiv.style.background = '#2ecc71';
                messageDiv.style.color = 'white';
                messageDiv.style.padding = '20px';
                messageDiv.style.borderRadius = '8px';
                messageDiv.style.boxShadow = '0 4px 10px rgba(0,0,0,0.3)';
                messageDiv.style.zIndex = '10000';
                messageDiv.style.textAlign = 'center';
                messageDiv.style.fontSize = '18px';
                messageDiv.style.fontWeight = 'bold';
                messageDiv.innerHTML = 'RESET COMPLETE<br>Reloading page in 3...';

                document.body.appendChild(messageDiv);

                // Countdown and then reload
                let countdown = 2;
                const interval = setInterval(function () {
                    messageDiv.innerHTML = `RESET COMPLETE<br>Reloading page in ${countdown}...`;
                    countdown--;

                    if (countdown < 0) {
                        clearInterval(interval);

                        // 11. FORCE RELOAD WITH CACHE CLEAR
                        console.log('[RESET] Performing hard reload');
                        window.location.href = window.location.href.split('?')[0] + '?reset=' + Date.now();
                    }
                }, 1000);

            } catch (error) {
                console.error('[RESET] Error during reset:', error);
                alert('Error during reset. Please try refreshing the page manually.');
            }
        }
    });

    console.log("Enhanced reset functionality installed");
})();