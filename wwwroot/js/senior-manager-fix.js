/**
 * Senior Manager Data Access Fix
 * 
 * This script ensures Senior Managers can view the same data as Admins
 * when accessing the Scrap Approval Dashboard with the same location settings.
 */

document.addEventListener('DOMContentLoaded', function () {
    console.log("Senior Manager Data Access Fix loading...");

    // Check if user is a Senior Manager
    const userType = document.querySelector('meta[name="user-type"]')?.content?.toLowerCase();
    console.log("Detected user type:", userType);

    if (userType === "seniormanager") {
        // Apply Senior Manager data access fix
        applySeniorManagerFix();
    }

    // Make sure the fix runs when location is updated
    const updateLocationBtn = document.getElementById('updateLocationBtn');
    if (updateLocationBtn) {
        updateLocationBtn.addEventListener('click', function () {
            setTimeout(checkAndRefreshData, 500);
        });
    }

    // Also check after location form is submitted
    const submitLocationBtn = document.getElementById('submitLocationBtn');
    if (submitLocationBtn) {
        submitLocationBtn.addEventListener('click', function () {
            setTimeout(checkAndRefreshData, 1000);
        });
    }
});

/**
 * Apply fixes specific to Senior Manager role
 */
function applySeniorManagerFix() {
    console.log("Applying Senior Manager data access fix");

    // 1. Check if we have "ALL" in the current location
    const currentDepartment = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const currentLine = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const currentShift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    console.log("Current location:", currentDepartment, currentLine, currentShift);

    // 2. If using "ALL" location, trigger data reload with admin parameter
    if (currentDepartment?.includes("ALL") ||
        currentLine?.includes("ALL") ||
        currentShift === "ALL") {
        console.log("ALL location detected, refreshing data with admin privileges");
        fetchDataWithAdminPrivileges();
    }

    // 3. Patch location form to ensure it keeps working for Senior Managers
    patchLocationForm();
}

/**
 * Check if location has "ALL" components and refresh data if needed
 */
function checkAndRefreshData() {
    const userType = document.querySelector('meta[name="user-type"]')?.content?.toLowerCase();
    if (userType !== "seniormanager") return;

    setTimeout(function () {
        const currentDepartment = document.querySelector('.location-value[data-department]')?.textContent.trim();
        const currentLine = document.querySelector('.location-value[data-line]')?.textContent.trim();
        const currentShift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

        console.log("Updated location:", currentDepartment, currentLine, currentShift);

        if (currentDepartment?.includes("ALL") ||
            currentLine?.includes("ALL") ||
            currentShift === "ALL") {
            console.log("ALL location detected after update, refreshing data");
            fetchDataWithAdminPrivileges();
        }
    }, 500); // Small delay to ensure DOM is updated
}

/**
 * Fetch dashboard data with admin privileges
 */
function fetchDataWithAdminPrivileges() {
    // Get the token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (!token) {
        console.error("CSRF token not found, cannot proceed with data fetch");
        return;
    }

    // Construct URL with parameters to load data with admin privileges
    const baseUrl = window.location.pathname;
    const url = `${baseUrl}?showAll=true`;

    console.log("Fetching data from:", url);

    // Show loading indicator
    showLoadingIndicator();

    // Fetch the dashboard data
    fetch(url, {
        headers: {
            'RequestVerificationToken': token,
            'X-Requested-With': 'XMLHttpRequest'
        }
    })
        .then(response => {
            if (!response.ok) {
                throw new Error(`Server returned ${response.status}: ${response.statusText}`);
            }
            return response.text();
        })
        .then(html => {
            // Parse stats from the returned HTML
            updateStatsFromHtml(html);

            // If we have a table, update the scrap items data
            updateScrapItemsFromHtml(html);

            hideLoadingIndicator();
        })
        .catch(error => {
            console.error("Error fetching data:", error);
            hideLoadingIndicator();

            // Show a notification to the user
            if (typeof showNotification === 'function') {
                showNotification("Error loading data. Please try refreshing the page.", "error");
            }
        });
}

/**
 * Extract and update stats values from the fetched HTML
 */
function updateStatsFromHtml(html) {
    try {
        // Create a temporary element to parse the HTML
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');

        // Find the stat values in the parsed document
        const statValues = doc.querySelectorAll('.stat-value');
        const currentStatValues = document.querySelectorAll('.stat-value');

        if (statValues.length >= 3 && currentStatValues.length >= 3) {
            // Update total items
            currentStatValues[0].textContent = statValues[0].textContent;

            // Update total cost
            currentStatValues[1].textContent = statValues[1].textContent;

            // Update pending approvals
            currentStatValues[2].textContent = statValues[2].textContent;

            console.log("Stats updated successfully");
        } else {
            console.warn("Stats elements not found in fetched HTML or current page");
        }
    } catch (error) {
        console.error("Error updating stats:", error);
    }
}

/**
 * Extract and update scrap items table from the fetched HTML
 */
function updateScrapItemsFromHtml(html) {
    try {
        // Create a temporary element to parse the HTML
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');

        // Find the scrap items table in the fetched HTML
        const sourceTable = doc.querySelector('.cyber-table');
        const targetTable = document.querySelector('.cyber-table');

        if (sourceTable && targetTable) {
            // Replace the tbody content
            const sourceTbody = sourceTable.querySelector('tbody');
            const targetTbody = targetTable.querySelector('tbody');

            if (sourceTbody && targetTbody) {
                targetTbody.innerHTML = sourceTbody.innerHTML;
                console.log("Scrap items table updated successfully");

                // Re-attach event handlers to checkboxes
                attachEventHandlersToTable();
            } else {
                console.warn("Table body not found in source or target table");
            }
        } else {
            console.warn("Scrap items table not found in fetched HTML or current page");
        }
    } catch (error) {
        console.error("Error updating scrap items table:", error);
    }
}

/**
 * Re-attach event handlers to the updated table
 */
function attachEventHandlersToTable() {
    // Re-attach checkbox handlers
    document.querySelectorAll('.row-checkbox').forEach(checkbox => {
        checkbox.addEventListener('change', function () {
            updateBulkActionButtons();

            // Toggle selected class on the row
            const row = this.closest('tr');
            if (row) {
                if (this.checked) {
                    row.classList.add('selected');
                } else {
                    row.classList.remove('selected');
                }
            }
        });
    });

    // Re-attach select all handler
    const selectAllCheckbox = document.getElementById('selectAll');
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', function () {
            const checkboxes = document.querySelectorAll('.row-checkbox');
            checkboxes.forEach(cb => {
                cb.checked = this.checked;

                // Toggle selected class on rows
                const row = cb.closest('tr');
                if (row) {
                    if (this.checked) {
                        row.classList.add('selected');
                    } else {
                        row.classList.remove('selected');
                    }
                }
            });

            updateBulkActionButtons();
        });
    }

    // Re-initialize action buttons
    document.querySelectorAll('.action-btn').forEach(btn => {
        if (btn.classList.contains('approve')) {
            btn.onclick = function () {
                const scrapId = this.getAttribute('data-id');
                if (scrapId && typeof handleApproval === 'function') {
                    handleApproval(scrapId, true);
                }
            };
        } else if (btn.classList.contains('reject')) {
            btn.onclick = function () {
                const scrapId = this.getAttribute('data-id');
                if (scrapId && typeof handleApproval === 'function') {
                    handleApproval(scrapId, false);
                }
            };
        } else if (btn.classList.contains('add-weight')) {
            btn.onclick = function () {
                const scrapId = this.getAttribute('data-scrap-id');
                if (scrapId && typeof showWeightPrompt === 'function') {
                    showWeightPrompt(scrapId);
                }
            };
        }
    });
}

/**
 * Update the state of bulk action buttons based on selected items
 */
function updateBulkActionButtons() {
    const selectedCheckboxes = document.querySelectorAll('.row-checkbox:checked');
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    const bulkRejectBtn = document.getElementById('bulk-reject-btn');

    if (bulkApproveBtn) {
        bulkApproveBtn.disabled = selectedCheckboxes.length === 0;
    }

    if (bulkRejectBtn) {
        bulkRejectBtn.disabled = selectedCheckboxes.length === 0;
    }
}

/**
 * Show loading indicator while fetching data
 */
function showLoadingIndicator() {
    // Check if a loading indicator already exists
    let loadingIndicator = document.getElementById('data-loading-indicator');

    if (!loadingIndicator) {
        // Create a loading indicator
        loadingIndicator = document.createElement('div');
        loadingIndicator.id = 'data-loading-indicator';
        loadingIndicator.style.position = 'fixed';
        loadingIndicator.style.top = '50%';
        loadingIndicator.style.left = '50%';
        loadingIndicator.style.transform = 'translate(-50%, -50%)';
        loadingIndicator.style.backgroundColor = 'rgba(0, 15, 14, 0.8)';
        loadingIndicator.style.padding = '20px';
        loadingIndicator.style.borderRadius = '8px';
        loadingIndicator.style.zIndex = '9999';
        loadingIndicator.style.border = '1px solid #00fffc';
        loadingIndicator.style.boxShadow = '0 0 10px #00fffc';
        loadingIndicator.style.color = '#00fffc';
        loadingIndicator.style.textAlign = 'center';
        loadingIndicator.innerHTML = `
            <div style="display: inline-block; animation: spin 1s linear infinite; margin-right: 10px;">
                <i class="bi bi-arrow-repeat" style="font-size: 1.5rem;"></i>
            </div>
            <div>Loading data...</div>
        `;

        document.body.appendChild(loadingIndicator);
    } else {
        loadingIndicator.style.display = 'block';
    }
}

/**
 * Hide loading indicator
 */
function hideLoadingIndicator() {
    const loadingIndicator = document.getElementById('data-loading-indicator');
    if (loadingIndicator) {
        loadingIndicator.style.display = 'none';
    }
}

/**
 * Patch location form to ensure it works correctly for Senior Managers
 */
function patchLocationForm() {
    // Add a hidden input field to the location form
    const locationForm = document.getElementById('locationForm');
    if (locationForm) {
        // Check if we already added our hidden field
        let hiddenInput = locationForm.querySelector('input[name="showAll"]');
        if (!hiddenInput) {
            hiddenInput = document.createElement('input');
            hiddenInput.type = 'hidden';
            hiddenInput.name = 'showAll';
            hiddenInput.value = 'true';
            locationForm.appendChild(hiddenInput);
            console.log("Added showAll parameter to location form");
        }
    }
}

// Create a notification function if one doesn't exist
if (typeof window.showNotification !== 'function') {
    window.showNotification = function (message, type = 'success') {
        console.log(`Notification (${type}): ${message}`);

        // Try to find toast container
        let toast = document.getElementById('notificationToast');
        let toastBody = document.getElementById('toastBody');

        if (toast && toastBody) {
            // Update toast content
            toastBody.textContent = message;

            // Update toast styling
            toast.className = toast.className.replace(/bg-\w+/, '');
            toast.classList.add(type === 'error' ? 'bg-danger' :
                type === 'warning' ? 'bg-warning' : 'bg-success');

            // Show the toast
            if (typeof bootstrap !== 'undefined' && bootstrap.Toast) {
                const bsToast = new bootstrap.Toast(toast);
                bsToast.show();
            } else {
                // Fallback for when Bootstrap is not available
                toast.style.display = 'block';
                setTimeout(() => {
                    toast.style.display = 'none';
                }, 5000);
            }
        } else {
            // Last resort fallback
            alert(message);
        }
    };
}