/**
 * Enhanced Weight Management System
 * Provides role-based functionality for both TeamLeader (single location) and Admin (bulk) weighing
 */

document.addEventListener('DOMContentLoaded', function () {
    console.log("Weight Management System loading...");

    // Detect user role first
    const userType = detectUserRole();
    console.log(`Detected user role: ${userType}`);

    // Initialize based on role
    if (userType === 'teamleader') {
        initializeTeamLeaderInterface();
    } else if (userType === 'admin' || userType === 'seniormanager') {
        initializeAdminInterface();

        // Hide the regular scrap management section as it's redundant
        const regularScrapSection = document.querySelector('.card:has(.cyber-table)');
        if (regularScrapSection) {
            regularScrapSection.style.display = 'none';
        }
    }

    // Common initialization for both roles
    fixButtonHandlers();
    setupNotifications();
});

/**
 * Detect user role from page elements
 */
function detectUserRole() {
    // First check the meta tag
    const userTypeMeta = document.querySelector('meta[name="user-type"]');
    if (userTypeMeta) {
        return userTypeMeta.getAttribute('content').toLowerCase();
    }

    // Fallback detection based on UI elements
    const hasAddWeightButtons = document.querySelectorAll('.add-weight').length > 0;
    const hasBulkWeightControls = document.getElementById('bulkWeightKg') !== null;

    if (hasAddWeightButtons && !hasBulkWeightControls) return 'teamleader';
    if (hasBulkWeightControls) return 'admin';

    return 'unknown';
}

/**
 * Initialize the TeamLeader interface for single location weight recording
 */
function initializeTeamLeaderInterface() {
    console.log("Initializing TeamLeader interface");

    // Ensure the weight entry section is visible
    const teamLeaderWeightSection = document.getElementById('teamLeaderWeightSection');
    if (teamLeaderWeightSection) {
        teamLeaderWeightSection.style.display = 'block';
    }

    // Fix the Save Weight button
    const saveLocationWeightBtn = document.getElementById('saveLocationWeightBtn');
    if (saveLocationWeightBtn) {
        // Clone and replace button to remove existing handlers
        const newSaveBtn = saveLocationWeightBtn.cloneNode(true);
        saveLocationWeightBtn.parentNode.replaceChild(newSaveBtn, saveLocationWeightBtn);

        // Add new click handler
        newSaveBtn.addEventListener('click', function () {
            saveTeamLeaderWeight();
        });
    }

    // Fix "Add Weight" buttons for approved items
    document.querySelectorAll('.add-weight').forEach(btn => {
        // Clone and replace button to remove existing handlers
        const newBtn = btn.cloneNode(true);
        btn.parentNode.replaceChild(newBtn, btn);

        // Add new click handler
        newBtn.addEventListener('click', function () {
            const scrapId = this.getAttribute('data-scrap-id');
            const department = this.getAttribute('data-department');
            const line = this.getAttribute('data-line');
            const shift = this.getAttribute('data-shift');

            showWeightPrompt(scrapId, department, line, shift);
        });
    });
}

/**
 * Initialize the Admin interface for bulk weighing
 */
function initializeAdminInterface() {
    console.log("Initializing Admin interface");

    // Load approved locations
    loadLocationsWithApprovedScrap();

    // Fix filter button
    const applyFilterBtn = document.getElementById('applyFilterBtn');
    if (applyFilterBtn) {
        // Clone and replace button to remove existing handlers
        const newFilterBtn = applyFilterBtn.cloneNode(true);
        applyFilterBtn.parentNode.replaceChild(newFilterBtn, applyFilterBtn);

        // Add new click handler
        newFilterBtn.addEventListener('click', function () {
            const department = document.getElementById('filterDepartment')?.value || 'All Departments';
            loadLocationsWithApprovedScrap(department);
        });
    }

    // Fix bulk save button
    const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');
    if (saveBulkWeightBtn) {
        // Clone and replace button to remove existing handlers
        const newSaveBtn = saveBulkWeightBtn.cloneNode(true);
        saveBulkWeightBtn.parentNode.replaceChild(newSaveBtn, saveBulkWeightBtn);

        // Add new click handler
        newSaveBtn.addEventListener('click', function () {
            saveBulkWeight();
        });
    }

    // Fix select-all checkbox
    const selectAllLocations = document.getElementById('selectAllLocations');
    if (selectAllLocations) {
        selectAllLocations.addEventListener('change', function () {
            toggleAllLocations();
        });
    }

    // Fix auto-select toggle
    const autoSelectToggle = document.getElementById('autoSelectToggle');
    if (autoSelectToggle) {
        autoSelectToggle.addEventListener('change', function () {
            if (this.checked) {
                selectApprovedLocations();
            }
        });
    }

    // Fix "Select All Approved" button
    const selectAllApprovedBtn = document.getElementById('selectAllApprovedBtn');
    if (selectAllApprovedBtn) {
        selectAllApprovedBtn.addEventListener('click', function () {
            selectApprovedLocations();
        });
    }
}

/**
 * Fix button handlers that might be broken
 */
function fixButtonHandlers() {
    // Fix any modals
    document.querySelectorAll('.btn-close-cyber, .modal-footer .btn-cyber-red').forEach(btn => {
        btn.onclick = function () {
            const modal = this.closest('.modal-cyber');
            if (modal) modal.style.display = 'none';
        };
    });

    // Fix location update button
    const updateLocationBtn = document.getElementById('updateLocationBtn');
    if (updateLocationBtn) {
        updateLocationBtn.onclick = function () {
            document.getElementById('locationModal').style.display = 'flex';
            setTimeout(populateLocationForm, 100);
        };
    }

    // Fix submit location button
    const submitLocationBtn = document.getElementById('submitLocationBtn');
    if (submitLocationBtn) {
        submitLocationBtn.addEventListener('click', function () {
            submitLocationForm();
        });
    }

    // Fix department-line relationship
    const deptSelect = document.getElementById('updateDepartment');
    if (deptSelect) {
        deptSelect.addEventListener('change', populateLineOptions);
    }
}

/**
 * Save weight for TeamLeader's current location
 */
function saveTeamLeaderWeight() {
    const weightInput = document.getElementById('locationWeightValue');
    const weight = weightInput?.value ? parseFloat(weightInput.value) : 0;

    if (!weight || weight <= 0) {
        showNotification('Please enter a valid weight greater than zero', 'error');
        return;
    }

    const description = document.getElementById('locationWeightDescription')?.value || '';

    // Get current location data
    const dept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const line = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const shift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    if (!dept || !line || !shift) {
        showNotification('Unable to determine current location', 'error');
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showNotification('Security token not found', 'error');
        return;
    }

    // Show loading state
    const saveBtn = document.getElementById('saveLocationWeightBtn');
    const originalText = saveBtn.innerHTML;
    saveBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
    saveBtn.disabled = true;

    // Save the weight
    fetch('/ScrapApproval/SaveWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Department: dept,
            Line: line,
            Shift: shift,
            Weight: weight,
            Description: description || `Weight record for ${dept}/${line}/${shift}`
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button
            saveBtn.innerHTML = originalText;
            saveBtn.disabled = false;

            if (data.success) {
                showNotification('Weight saved successfully', 'success');
                weightInput.value = '';
                document.getElementById('locationWeightDescription').value = '';
            } else {
                showNotification(data.message || 'Error saving weight', 'error');
            }
        })
        .catch(error => {
            console.error('Error saving weight:', error);
            saveBtn.innerHTML = originalText;
            saveBtn.disabled = false;
            showNotification('Error saving weight. Please try again.', 'error');
        });
}

/**
 * Load locations with approved scrap items for bulk weighing
 */
function loadLocationsWithApprovedScrap(department = null) {
    console.log("Loading locations with approved scrap...");

    // Show loading state
    const loadingElement = document.getElementById('locationsLoading');
    const tableElement = document.getElementById('locationsTable');
    const emptyState = document.getElementById('locationsEmptyState');

    if (loadingElement) loadingElement.style.display = 'block';
    if (tableElement) tableElement.style.display = 'none';
    if (emptyState) emptyState.style.display = 'none';

    // Get token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Build API URL with cache-busting
    let url = '/ScrapApproval/GetLocationsWithApprovedScrap';
    if (department && department !== 'All Departments') {
        url += `?department=${encodeURIComponent(department)}`;
    }
    url += (url.includes('?') ? '&' : '?') + 'nocache=' + new Date().getTime();

    // Make request
    fetch(url, {
        headers: {
            'RequestVerificationToken': token,
            'Cache-Control': 'no-cache'
        }
    })
        .then(response => response.json())
        .then(data => {
            if (loadingElement) loadingElement.style.display = 'none';

            const tableBody = document.getElementById('locationsTableBody');

            if (!tableBody) {
                console.error("Locations table body not found");
                return;
            }

            if (data.success && data.locations && data.locations.length > 0) {
                // Clear existing rows
                tableBody.innerHTML = '';

                // Filter to only keep locations with unweighed items
                const filteredLocations = data.locations.filter(loc =>
                    loc.unweighedScrapCount > 0 || (loc.approvedItemCount > 0 && loc.lastWeighedDate === 'Never')
                );

                if (filteredLocations.length > 0) {
                    // Add each location row
                    filteredLocations.forEach(loc => {
                        // Highlight rows that are ready for weighing
                        const rowClass = loc.unweighedScrapCount > 0 ? 'ready-for-weighing' : '';

                        const row = document.createElement('tr');
                        if (rowClass) row.className = rowClass;

                        row.innerHTML = `
                        <td>
                            <input type="checkbox" class="location-checkbox" 
                                data-department="${escapeHtml(loc.department)}" 
                                data-line="${escapeHtml(loc.line)}" 
                                data-shift="${escapeHtml(loc.shift)}"
                                data-item-count="${loc.approvedItemCount}"
                                data-unweighed="${loc.unweighedScrapCount}"
                                data-should-select="${loc.shouldAutoSelect ? 'true' : 'false'}"
                                onchange="updateSelectedLocations()">
                        </td>
                        <td>${escapeHtml(loc.department)}</td>
                        <td>${escapeHtml(loc.line)}</td>
                        <td>${escapeHtml(loc.shift)}</td>
                        <td>${loc.approvedItemCount}</td>
                        <td>${loc.lastWeighedDate || 'Never'}</td>
                    `;
                        tableBody.appendChild(row);
                    });

                    // Show the table
                    if (tableElement) tableElement.style.display = 'block';

                    // Auto-select approved locations if toggle is checked
                    if (document.getElementById('autoSelectToggle')?.checked) {
                        selectApprovedLocations();
                    }

                    console.log(`Loaded ${filteredLocations.length} locations with approved scrap`);
                } else {
                    // No locations with unweighed items
                    if (emptyState) emptyState.style.display = 'block';
                    if (tableElement) tableElement.style.display = 'block';
                }
            } else {
                // No locations found
                if (emptyState) emptyState.style.display = 'block';
                if (tableElement) tableElement.style.display = 'block';
            }
        })
        .catch(error => {
            console.error('Error loading locations:', error);

            // Hide loading state
            if (loadingElement) loadingElement.style.display = 'none';
            if (emptyState) {
                emptyState.style.display = 'block';
                emptyState.innerHTML = '<p class="mb-0 text-danger">Error loading locations. Please try again.</p>';
            }
            if (tableElement) tableElement.style.display = 'block';
        });
}

/**
 * Auto-select approved locations
 */
function selectApprovedLocations() {
    const locationCheckboxes = document.querySelectorAll('.location-checkbox');

    locationCheckboxes.forEach(cb => {
        // Auto-select if it has unweighed items or should be selected based on the server's recommendation
        const unweighedCount = parseInt(cb.getAttribute('data-unweighed') || '0');
        const shouldSelect = cb.getAttribute('data-should-select') === 'true';

        if (unweighedCount > 0 || shouldSelect) {
            cb.checked = true;
        }
    });

    updateSelectedLocations();
}

/**
 * Toggle all locations selection
 */
function toggleAllLocations() {
    const selectAllCheckbox = document.getElementById('selectAllLocations');
    const locationCheckboxes = document.querySelectorAll('.location-checkbox');

    locationCheckboxes.forEach(cb => {
        cb.checked = selectAllCheckbox.checked;
    });

    updateSelectedLocations();
}

/**
 * Update selected locations summary display
 */
function updateSelectedLocations() {
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');
    const selectedContainer = document.getElementById('selectedLocationsContainer');
    const noLocationsSelected = document.getElementById('noLocationsSelected');
    const selectedLocationsList = document.getElementById('selectedLocationsList');
    const selectedLocationsCount = document.getElementById('selectedLocationsCount');
    const totalItemsCount = document.getElementById('totalItemsCount');
    const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');

    // Update visibility based on selection
    if (selectedCheckboxes.length === 0) {
        if (selectedContainer) selectedContainer.style.display = 'none';
        if (noLocationsSelected) noLocationsSelected.style.display = 'block';

        if (saveBulkWeightBtn) {
            saveBulkWeightBtn.disabled = true;
            saveBulkWeightBtn.classList.add('disabled');
        }
        return;
    }

    // Show selected locations
    if (selectedContainer) selectedContainer.style.display = 'block';
    if (noLocationsSelected) noLocationsSelected.style.display = 'none';

    if (saveBulkWeightBtn) {
        saveBulkWeightBtn.disabled = false;
        saveBulkWeightBtn.classList.remove('disabled');
    }

    // Update count
    if (selectedLocationsCount) {
        selectedLocationsCount.textContent = selectedCheckboxes.length;
    }

    // Build list of selected locations
    if (selectedLocationsList) {
        selectedLocationsList.innerHTML = '';

        let totalItems = 0;

        selectedCheckboxes.forEach(checkbox => {
            const dept = checkbox.dataset.department;
            const line = checkbox.dataset.line;
            const shift = checkbox.dataset.shift;
            const itemCount = parseInt(checkbox.dataset.itemCount || '0');

            totalItems += itemCount;

            const locationItem = document.createElement('div');
            locationItem.className = 'mb-2 p-2 border rounded bg-dark text-info';
            locationItem.innerHTML = `
                <div class="d-flex justify-content-between align-items-center">
                    <div>
                        <span class="fw-bold">${escapeHtml(dept)}/${escapeHtml(line)}/${escapeHtml(shift)}</span>
                        <span class="badge bg-secondary ms-2">${itemCount} items</span>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-danger remove-location-btn">
                        <i class="bi bi-x"></i>
                    </button>
                </div>
            `;

            // Add remove button handler
            locationItem.querySelector('.remove-location-btn').addEventListener('click', function () {
                checkbox.checked = false;
                updateSelectedLocations();
            });

            selectedLocationsList.appendChild(locationItem);
        });

        // Update total items count
        if (totalItemsCount) {
            totalItemsCount.textContent = totalItems;
        }
    }
}

/**
 * Save bulk weight for selected locations
 */
function saveBulkWeight() {
    // Get selected locations
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        showNotification('Please select at least one location', 'error');
        return;
    }

    // Get weight value
    const weightInput = document.getElementById('bulkWeightKg');
    const weight = parseFloat(weightInput?.value);

    if (!weight || weight <= 0) {
        showNotification('Please enter a valid weight greater than zero', 'error');
        return;
    }

    // Get description and token
    const description = document.getElementById('bulkWeightDescription')?.value || '';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showNotification('Security token not found', 'error');
        return;
    }

    // Build locations array
    const locations = [];
    selectedCheckboxes.forEach(cb => {
        // Ensure shift value doesn't exceed database limit
        let shift = cb.getAttribute('data-shift') || '';
        if (shift.length > 30) shift = shift.substring(0, 30);

        locations.push({
            Department: cb.getAttribute('data-department'),
            Line: cb.getAttribute('data-line'),
            Shift: shift
        });
    });

    // Update button state
    const saveBtn = document.getElementById('saveBulkWeightBtn');
    let originalText = '';

    if (saveBtn) {
        originalText = saveBtn.innerHTML;
        saveBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
        saveBtn.disabled = true;
    }

    // Submit bulk weight
    fetch('/ScrapApproval/SaveBulkWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Locations: locations,
            Weight: weight,
            Description: description || `Bulk weighing for ${locations.length} locations`
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .then(data => {
            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            if (data.success) {
                // Show success notification
                showNotification('Weight saved successfully', 'success');

                // Reset form
                if (weightInput) weightInput.value = '';

                const descInput = document.getElementById('bulkWeightDescription');
                if (descInput) descInput.value = '';

                // Reset selections
                document.getElementById('selectAllLocations').checked = false;
                selectedCheckboxes.forEach(cb => {
                    cb.checked = false;
                });
                updateSelectedLocations();

                // Refresh data
                setTimeout(() => {
                    loadLocationsWithApprovedScrap();
                    loadWeightHistory();
                }, 500);
            } else {
                showNotification(data.message || 'Error saving weight', 'error');
            }
        })
        .catch(error => {
            console.error('Error saving bulk weight:', error);

            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            showNotification('Error saving weight. Please try again.', 'error');
        });
}

/**
 * Show weight prompt modal for TeamLeader
 */
function showWeightPrompt(scrapId, department, line, shift) {
    // Find the modal
    const modal = document.getElementById('weightUpdateModal');
    if (!modal) {
        console.error("Weight update modal not found");
        return;
    }

    // Set scrap ID in the form
    document.getElementById('weightScrapId').value = scrapId;

    // Store location info as data attributes if provided
    if (department && line && shift) {
        modal.setAttribute('data-department', department);
        modal.setAttribute('data-line', line);
        modal.setAttribute('data-shift', shift);
    }

    // Clear previous value and error
    document.getElementById('weightValue').value = '';
    const errorMessage = modal.querySelector('.error-message');
    if (errorMessage) errorMessage.style.display = 'none';

    // Show the modal
    modal.style.display = 'flex';

    // Focus on weight input
    setTimeout(() => {
        document.getElementById('weightValue').focus();
    }, 100);

    // Fix confirm button
    const confirmBtn = document.getElementById('confirmWeightBtn');
    if (confirmBtn) {
        const newBtn = confirmBtn.cloneNode(true);
        confirmBtn.parentNode.replaceChild(newBtn, confirmBtn);

        newBtn.addEventListener('click', function () {
            submitWeightUpdate();
        });
    }
}

/**
 * Submit weight update from modal
 */
function submitWeightUpdate() {
    const scrapId = document.getElementById('weightScrapId').value;
    const weight = parseFloat(document.getElementById('weightValue').value);
    const modal = document.getElementById('weightUpdateModal');

    if (!weight || weight <= 0) {
        const errorMessage = modal.querySelector('.error-message');
        if (errorMessage) {
            errorMessage.textContent = 'Please enter a valid weight greater than zero';
            errorMessage.style.display = 'block';
        }
        return;
    }

    // Get data from either modal attributes or data-attributes on the button
    const department = modal.getAttribute('data-department');
    const line = modal.getAttribute('data-line');
    const shift = modal.getAttribute('data-shift');

    // Get CSRF token
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    // Show loading state
    const confirmBtn = document.getElementById('confirmWeightBtn');
    const originalText = confirmBtn.innerHTML;
    confirmBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
    confirmBtn.disabled = true;

    // Submit weight using the AddWeight endpoint
    fetch('/ScrapApproval/AddWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapId: parseInt(scrapId),
            Department: department,
            Line: line,
            Shift: shift,
            Weight: weight
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            confirmBtn.innerHTML = originalText;
            confirmBtn.disabled = false;

            if (data.success) {
                // Close modal
                hideModal('weightUpdateModal');

                // Show success
                showNotification('Weight saved successfully', 'success');

                // Refresh data after successful save
                if (typeof loadWeightHistory === 'function') {
                    loadWeightHistory();
                }
            } else {
                // Show error
                const errorMessage = modal.querySelector('.error-message');
                if (errorMessage) {
                    errorMessage.textContent = data.message || 'Error saving weight';
                    errorMessage.style.display = 'block';
                } else {
                    showNotification(data.message || 'Error saving weight', 'error');
                }
            }
        })
        .catch(error => {
            console.error('Error saving weight:', error);

            // Reset button state
            confirmBtn.innerHTML = originalText;
            confirmBtn.disabled = false;

            // Show error
            const errorMessage = modal.querySelector('.error-message');
            if (errorMessage) {
                errorMessage.textContent = 'Error saving weight. Please try again.';
                errorMessage.style.display = 'block';
            } else {
                showNotification('Error saving weight. Please try again.', 'error');
            }
        });
}

/**
 * Load weight history for current location
 */
function loadWeightHistory() {
    // Get current location
    const department = document.querySelector('.location-value[data-department]')?.getAttribute('data-department');
    const line = document.querySelector('.location-value[data-line]')?.getAttribute('data-line');
    const shift = document.querySelector('.location-value[data-shift]')?.getAttribute('data-shift');

    if (!department || !line || !shift) {
        console.log("Missing location information for weight history");
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    fetch(`/ScrapApproval/GetWeightHistory?department=${encodeURIComponent(department)}&line=${encodeURIComponent(line)}&shift=${encodeURIComponent(shift)}`, {
        headers: {
            'RequestVerificationToken': token
        }
    })
        .then(response => response.json())
        .then(data => {
            const historyBody = document.getElementById('weightHistoryTableBody');
            const emptyState = document.getElementById('weightHistoryEmpty');

            if (!historyBody) return;

            if (data.success && data.history && data.history.length > 0) {
                historyBody.innerHTML = '';

                data.history.forEach(item => {
                    const locationDisplay = item.IsBulkWeighing ?
                        item.Locations :
                        `${item.Department}/${item.Line}/${item.Shift}`;

                    const row = document.createElement('tr');
                    row.innerHTML = `
                    <td>${escapeHtml(locationDisplay)}</td>
                    <td>${item.Weight} kg</td>
                    <td>${escapeHtml(item.Description || '')}</td>
                    <td>${item.CreatedAt}</td>
                    <td>${escapeHtml(item.CreatedBy)}</td>
                `;
                    historyBody.appendChild(row);
                });

                if (emptyState) emptyState.style.display = 'none';
            } else {
                historyBody.innerHTML = '<tr><td colspan="5" class="text-center">No weight history found for this location.</td></tr>';
                if (emptyState) emptyState.style.display = 'block';
            }
        })
        .catch(error => {
            console.error('Error loading weight history:', error);

            const historyBody = document.getElementById('weightHistoryTableBody');
            if (historyBody) {
                historyBody.innerHTML = '<tr><td colspan="5" class="text-center text-danger">Error loading weight history</td></tr>';
            }
        });
}

/**
 * Populate location form with current values
 */
function populateLocationForm() {
    const currentDept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const currentLine = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const currentShift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    // Set department
    const deptSelect = document.getElementById('updateDepartment');
    if (deptSelect && currentDept) {
        for (let i = 0; i < deptSelect.options.length; i++) {
            if (deptSelect.options[i].value === currentDept ||
                deptSelect.options[i].text === currentDept) {
                deptSelect.selectedIndex = i;
                break;
            }
        }

        // Trigger change event to populate lines
        const event = new Event('change');
        deptSelect.dispatchEvent(event);
    }

    // Set line with delay (to allow for lines to load)
    setTimeout(function () {
        const lineSelect = document.getElementById('updateLine');
        if (lineSelect && currentLine) {
            for (let i = 0; i < lineSelect.options.length; i++) {
                if (lineSelect.options[i].value === currentLine ||
                    lineSelect.options[i].text === currentLine) {
                    lineSelect.selectedIndex = i;
                    break;
                }
            }
        }

        // Set shift
        const shiftSelect = document.getElementById('updateShift');
        if (shiftSelect && currentShift) {
            for (let i = 0; i < shiftSelect.options.length; i++) {
                if (shiftSelect.options[i].value === currentShift ||
                    shiftSelect.options[i].text === currentShift) {
                    shiftSelect.selectedIndex = i;
                    break;
                }
            }
        }
    }, 300);
}

/**
 * Populate line options based on selected department
 */
function populateLineOptions() {
    const department = document.getElementById('updateDepartment').value;
    const lineSelect = document.getElementById('updateLine');

    // Clear existing options
    while (lineSelect.options.length > 1) {
        lineSelect.remove(1);
    }

    // Exit if no department selected
    if (!department) {
        lineSelect.options[0].text = "Select a department first";
        return;
    }

    // Set first option text
    lineSelect.options[0].text = "Select Line";

    // Get line options from hidden data
    const lineOptions = document.querySelectorAll(`#line-options-data option[data-dept="${department}"]`);

    // Add matching options
    lineOptions.forEach(function (option) {
        var newOption = document.createElement('option');
        newOption.value = option.value;
        newOption.text = option.text || option.value;
        lineSelect.add(newOption);
    });

    if (lineOptions.length === 0) {
        lineSelect.options[0].text = "No lines found";
    }
}

/**
 * Submit location form
 */
function submitLocationForm() {
    // Get form values
    const dept = document.getElementById('updateDepartment')?.value;
    const line = document.getElementById('updateLine')?.value;
    const shift = document.getElementById('updateShift')?.value;

    // Validate form
    if (!dept || !line || !shift) {
        showErrorMessage('locationError', "All fields are required");
        return;
    }

    // Show loading state
    const submitBtn = document.getElementById('submitLocationBtn');
    const btnText = submitBtn.querySelector('.button-text');
    const spinner = submitBtn.querySelector('.spinner');

    if (btnText) btnText.style.display = 'none';
    if (spinner) spinner.style.display = 'inline-block';
    submitBtn.disabled = true;

    // Get token from hidden input
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    // Create a form element
    const form = document.createElement('form');
    form.method = 'post';
    form.action = '/ScrapApproval/UpdateLocationForm';
    form.style.display = 'none';

    // Add fields
    const addField = (name, value) => {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = name;
        input.value = value;
        form.appendChild(input);
    };

    addField('Department', dept);
    addField('Line', line);
    addField('Shift', shift);
    addField('__RequestVerificationToken', token);

    // Add form to document, submit it, then remove it
    document.body.appendChild(form);
    form.submit();

    // Hide modal to prevent double submission
    document.getElementById('locationModal').style.display = 'none';
}

/**
 * Show notification toast
 */
function showNotification(message, type = 'success') {
    const toast = document.getElementById('notificationToast');
    const toastBody = document.getElementById('toastBody');

    if (toast && toastBody) {
        // Set content
        toastBody.textContent = message;

        // Set type
        toast.className = toast.className.replace(/bg-\w+/, '');
        toast.classList.add(type === 'error' ? 'bg-danger' :
            type === 'warning' ? 'bg-warning' : 'bg-success');

        // Check if we have the Bootstrap Toast component
        if (typeof bootstrap !== 'undefined' && bootstrap.Toast) {
            const bsToast = new bootstrap.Toast(toast);
            bsToast.show();
        } else {
            // Fallback if Bootstrap is not available
            toast.style.display = 'block';
            setTimeout(() => { toast.style.display = 'none'; }, 5000);
        }
    } else {
        // Fallback to alert
        alert(message);
    }
}

/**
 * Show error message in a form or container
 */
function showErrorMessage(elementId, message) {
    const errorElement = document.getElementById(elementId);
    if (errorElement) {
        errorElement.textContent = message;
        errorElement.style.display = 'block';
    } else {
        showNotification(message, 'error');
    }
}

/**
 * Hide a modal by ID
 */
function hideModal(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        modal.style.display = 'none';
    }
}

/**
 * Setup notification system
 */
function setupNotifications() {
    // Create toast container if not exists
    if (!document.getElementById('notificationToast')) {
        const toastContainer = document.createElement('div');
        toastContainer.className = 'position-fixed bottom-0 end-0 p-3';
        toastContainer.style.zIndex = '11';

        toastContainer.innerHTML = `
            <div id="notificationToast" class="toast align-items-center text-white bg-success" role="alert" aria-live="assertive" aria-atomic="true">
                <div class="d-flex">
                    <div id="toastBody" class="toast-body">
                        Weight saved successfully!
                    </div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
                </div>
            </div>
        `;

        document.body.appendChild(toastContainer);
    }
}

/**
 * Escape HTML special characters to prevent XSS
 */
function escapeHtml(text) {
    if (!text) return '';

    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// Make helper functions globally available
window.showWeightPrompt = showWeightPrompt;
window.toggleAllLocations = toggleAllLocations;
window.updateSelectedLocations = updateSelectedLocations;
window.selectApprovedLocations = selectApprovedLocations;
window.hideModal = hideModal;
window.populateLineOptions = populateLineOptions;
window.submitLocationForm = submitLocationForm;
window.submitWeightUpdate = submitWeightUpdate;
window.saveTeamLeaderWeight = saveTeamLeaderWeight;/**
 * Enhanced Weight Management System
 * Provides role-based functionality for both TeamLeader (single location) and Admin (bulk) weighing
 */

document.addEventListener('DOMContentLoaded', function () {
    console.log("Weight Management System loading...");

    // Detect user role first
    const userType = detectUserRole();
    console.log(`Detected user role: ${userType}`);

    // Initialize based on role
    if (userType === 'teamleader') {
        initializeTeamLeaderInterface();
    } else if (userType === 'admin' || userType === 'seniormanager') {
        initializeAdminInterface();

        // Hide the regular scrap management section as it's redundant
        const regularScrapSection = document.querySelector('.card:has(.cyber-table)');
        if (regularScrapSection) {
            regularScrapSection.style.display = 'none';
        }
    }

    // Common initialization for both roles
    fixButtonHandlers();
    setupNotifications();
});

/**
 * Detect user role from page elements
 */
function detectUserRole() {
    // First check the meta tag
    const userTypeMeta = document.querySelector('meta[name="user-type"]');
    if (userTypeMeta) {
        return userTypeMeta.getAttribute('content').toLowerCase();
    }

    // Fallback detection based on UI elements
    const hasAddWeightButtons = document.querySelectorAll('.add-weight').length > 0;
    const hasBulkWeightControls = document.getElementById('bulkWeightKg') !== null;

    if (hasAddWeightButtons && !hasBulkWeightControls) return 'teamleader';
    if (hasBulkWeightControls) return 'admin';

    return 'unknown';
}

/**
 * Initialize the TeamLeader interface for single location weight recording
 */
function initializeTeamLeaderInterface() {
    console.log("Initializing TeamLeader interface");

    // Ensure the weight entry section is visible
    const teamLeaderWeightSection = document.getElementById('teamLeaderWeightSection');
    if (teamLeaderWeightSection) {
        teamLeaderWeightSection.style.display = 'block';
    }

    // Fix the Save Weight button
    const saveLocationWeightBtn = document.getElementById('saveLocationWeightBtn');
    if (saveLocationWeightBtn) {
        // Clone and replace button to remove existing handlers
        const newSaveBtn = saveLocationWeightBtn.cloneNode(true);
        saveLocationWeightBtn.parentNode.replaceChild(newSaveBtn, saveLocationWeightBtn);

        // Add new click handler
        newSaveBtn.addEventListener('click', function () {
            saveTeamLeaderWeight();
        });
    }

    // Fix "Add Weight" buttons for approved items
    document.querySelectorAll('.add-weight').forEach(btn => {
        // Clone and replace button to remove existing handlers
        const newBtn = btn.cloneNode(true);
        btn.parentNode.replaceChild(newBtn, btn);

        // Add new click handler
        newBtn.addEventListener('click', function () {
            const scrapId = this.getAttribute('data-scrap-id');
            const department = this.getAttribute('data-department');
            const line = this.getAttribute('data-line');
            const shift = this.getAttribute('data-shift');

            showWeightPrompt(scrapId, department, line, shift);
        });
    });
}

/**
 * Initialize the Admin interface for bulk weighing
 */
function initializeAdminInterface() {
    console.log("Initializing Admin interface");

    // Load approved locations
    loadLocationsWithApprovedScrap();

    // Fix filter button
    const applyFilterBtn = document.getElementById('applyFilterBtn');
    if (applyFilterBtn) {
        // Clone and replace button to remove existing handlers
        const newFilterBtn = applyFilterBtn.cloneNode(true);
        applyFilterBtn.parentNode.replaceChild(newFilterBtn, applyFilterBtn);

        // Add new click handler
        newFilterBtn.addEventListener('click', function () {
            const department = document.getElementById('filterDepartment')?.value || 'All Departments';
            loadLocationsWithApprovedScrap(department);
        });
    }

    // Fix bulk save button
    const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');
    if (saveBulkWeightBtn) {
        // Clone and replace button to remove existing handlers
        const newSaveBtn = saveBulkWeightBtn.cloneNode(true);
        saveBulkWeightBtn.parentNode.replaceChild(newSaveBtn, saveBulkWeightBtn);

        // Add new click handler
        newSaveBtn.addEventListener('click', function () {
            saveBulkWeight();
        });
    }

    // Fix select-all checkbox
    const selectAllLocations = document.getElementById('selectAllLocations');
    if (selectAllLocations) {
        selectAllLocations.addEventListener('change', function () {
            toggleAllLocations();
        });
    }

    // Fix auto-select toggle
    const autoSelectToggle = document.getElementById('autoSelectToggle');
    if (autoSelectToggle) {
        autoSelectToggle.addEventListener('change', function () {
            if (this.checked) {
                selectApprovedLocations();
            }
        });
    }

    // Fix "Select All Approved" button
    const selectAllApprovedBtn = document.getElementById('selectAllApprovedBtn');
    if (selectAllApprovedBtn) {
        selectAllApprovedBtn.addEventListener('click', function () {
            selectApprovedLocations();
        });
    }
}

/**
 * Fix button handlers that might be broken
 */
function fixButtonHandlers() {
    // Fix any modals
    document.querySelectorAll('.btn-close-cyber, .modal-footer .btn-cyber-red').forEach(btn => {
        btn.onclick = function () {
            const modal = this.closest('.modal-cyber');
            if (modal) modal.style.display = 'none';
        };
    });

    // Fix location update button
    const updateLocationBtn = document.getElementById('updateLocationBtn');
    if (updateLocationBtn) {
        updateLocationBtn.onclick = function () {
            document.getElementById('locationModal').style.display = 'flex';
            setTimeout(populateLocationForm, 100);
        };
    }

    // Fix submit location button
    const submitLocationBtn = document.getElementById('submitLocationBtn');
    if (submitLocationBtn) {
        submitLocationBtn.addEventListener('click', function () {
            submitLocationForm();
        });
    }

    // Fix department-line relationship
    const deptSelect = document.getElementById('updateDepartment');
    if (deptSelect) {
        deptSelect.addEventListener('change', populateLineOptions);
    }
}

/**
 * Save weight for TeamLeader's current location
 */
function saveTeamLeaderWeight() {
    const weightInput = document.getElementById('locationWeightValue');
    const weight = weightInput?.value ? parseFloat(weightInput.value) : 0;

    if (!weight || weight <= 0) {
        showNotification('Please enter a valid weight greater than zero', 'error');
        return;
    }

    const description = document.getElementById('locationWeightDescription')?.value || '';

    // Get current location data
    const dept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const line = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const shift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    if (!dept || !line || !shift) {
        showNotification('Unable to determine current location', 'error');
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showNotification('Security token not found', 'error');
        return;
    }

    // Show loading state
    const saveBtn = document.getElementById('saveLocationWeightBtn');
    const originalText = saveBtn.innerHTML;
    saveBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
    saveBtn.disabled = true;

    // Save the weight
    fetch('/ScrapApproval/SaveWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Department: dept,
            Line: line,
            Shift: shift,
            Weight: weight,
            Description: description || `Weight record for ${dept}/${line}/${shift}`
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button
            saveBtn.innerHTML = originalText;
            saveBtn.disabled = false;

            if (data.success) {
                showNotification('Weight saved successfully', 'success');
                weightInput.value = '';
                document.getElementById('locationWeightDescription').value = '';
            } else {
                showNotification(data.message || 'Error saving weight', 'error');
            }
        })
        .catch(error => {
            console.error('Error saving weight:', error);
            saveBtn.innerHTML = originalText;
            saveBtn.disabled = false;
            showNotification('Error saving weight. Please try again.', 'error');
        });
}

/**
 * Load locations with approved scrap items for bulk weighing
 */
function loadLocationsWithApprovedScrap(department = null) {
    console.log("Loading locations with approved scrap...");

    // Show loading state
    const loadingElement = document.getElementById('locationsLoading');
    const tableElement = document.getElementById('locationsTable');
    const emptyState = document.getElementById('locationsEmptyState');

    if (loadingElement) loadingElement.style.display = 'block';
    if (tableElement) tableElement.style.display = 'none';
    if (emptyState) emptyState.style.display = 'none';

    // Get token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Build API URL with cache-busting
    let url = '/ScrapApproval/GetLocationsWithApprovedScrap';
    if (department && department !== 'All Departments') {
        url += `?department=${encodeURIComponent(department)}`;
    }
    url += (url.includes('?') ? '&' : '?') + 'nocache=' + new Date().getTime();

    // Make request
    fetch(url, {
        headers: {
            'RequestVerificationToken': token,
            'Cache-Control': 'no-cache'
        }
    })
        .then(response => response.json())
        .then(data => {
            if (loadingElement) loadingElement.style.display = 'none';

            const tableBody = document.getElementById('locationsTableBody');

            if (!tableBody) {
                console.error("Locations table body not found");
                return;
            }

            if (data.success && data.locations && data.locations.length > 0) {
                // Clear existing rows
                tableBody.innerHTML = '';

                // Filter to only keep locations with unweighed items
                const filteredLocations = data.locations.filter(loc =>
                    loc.unweighedScrapCount > 0 || (loc.approvedItemCount > 0 && loc.lastWeighedDate === 'Never')
                );

                if (filteredLocations.length > 0) {
                    // Add each location row
                    filteredLocations.forEach(loc => {
                        // Highlight rows that are ready for weighing
                        const rowClass = loc.unweighedScrapCount > 0 ? 'ready-for-weighing' : '';

                        const row = document.createElement('tr');
                        if (rowClass) row.className = rowClass;

                        row.innerHTML = `
                        <td>
                            <input type="checkbox" class="location-checkbox" 
                                data-department="${escapeHtml(loc.department)}" 
                                data-line="${escapeHtml(loc.line)}" 
                                data-shift="${escapeHtml(loc.shift)}"
                                data-item-count="${loc.approvedItemCount}"
                                data-unweighed="${loc.unweighedScrapCount}"
                                data-should-select="${loc.shouldAutoSelect ? 'true' : 'false'}"
                                onchange="updateSelectedLocations()">
                        </td>
                        <td>${escapeHtml(loc.department)}</td>
                        <td>${escapeHtml(loc.line)}</td>
                        <td>${escapeHtml(loc.shift)}</td>
                        <td>${loc.approvedItemCount}</td>
                        <td>${loc.lastWeighedDate || 'Never'}</td>
                    `;
                        tableBody.appendChild(row);
                    });

                    // Show the table
                    if (tableElement) tableElement.style.display = 'block';

                    // Auto-select approved locations if toggle is checked
                    if (document.getElementById('autoSelectToggle')?.checked) {
                        selectApprovedLocations();
                    }

                    console.log(`Loaded ${filteredLocations.length} locations with approved scrap`);
                } else {
                    // No locations with unweighed items
                    if (emptyState) emptyState.style.display = 'block';
                    if (tableElement) tableElement.style.display = 'block';
                }
            } else {
                // No locations found
                if (emptyState) emptyState.style.display = 'block';
                if (tableElement) tableElement.style.display = 'block';
            }
        })
        .catch(error => {
            console.error('Error loading locations:', error);

            // Hide loading state
            if (loadingElement) loadingElement.style.display = 'none';
            if (emptyState) {
                emptyState.style.display = 'block';
                emptyState.innerHTML = '<p class="mb-0 text-danger">Error loading locations. Please try again.</p>';
            }
            if (tableElement) tableElement.style.display = 'block';
        });
}

/**
 * Auto-select approved locations
 */
function selectApprovedLocations() {
    const locationCheckboxes = document.querySelectorAll('.location-checkbox');

    locationCheckboxes.forEach(cb => {
        // Auto-select if it has unweighed items or should be selected based on the server's recommendation
        const unweighedCount = parseInt(cb.getAttribute('data-unweighed') || '0');
        const shouldSelect = cb.getAttribute('data-should-select') === 'true';

        if (unweighedCount > 0 || shouldSelect) {
            cb.checked = true;
        }
    });

    updateSelectedLocations();
}

/**
 * Toggle all locations selection
 */
function toggleAllLocations() {
    const selectAllCheckbox = document.getElementById('selectAllLocations');
    const locationCheckboxes = document.querySelectorAll('.location-checkbox');

    locationCheckboxes.forEach(cb => {
        cb.checked = selectAllCheckbox.checked;
    });

    updateSelectedLocations();
}

/**
 * Update selected locations summary display
 */
function updateSelectedLocations() {
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');
    const selectedContainer = document.getElementById('selectedLocationsContainer');
    const noLocationsSelected = document.getElementById('noLocationsSelected');
    const selectedLocationsList = document.getElementById('selectedLocationsList');
    const selectedLocationsCount = document.getElementById('selectedLocationsCount');
    const totalItemsCount = document.getElementById('totalItemsCount');
    const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');

    // Update visibility based on selection
    if (selectedCheckboxes.length === 0) {
        if (selectedContainer) selectedContainer.style.display = 'none';
        if (noLocationsSelected) noLocationsSelected.style.display = 'block';

        if (saveBulkWeightBtn) {
            saveBulkWeightBtn.disabled = true;
            saveBulkWeightBtn.classList.add('disabled');
        }
        return;
    }

    // Show selected locations
    if (selectedContainer) selectedContainer.style.display = 'block';
    if (noLocationsSelected) noLocationsSelected.style.display = 'none';

    if (saveBulkWeightBtn) {
        saveBulkWeightBtn.disabled = false;
        saveBulkWeightBtn.classList.remove('disabled');
    }

    // Update count
    if (selectedLocationsCount) {
        selectedLocationsCount.textContent = selectedCheckboxes.length;
    }

    // Build list of selected locations
    if (selectedLocationsList) {
        selectedLocationsList.innerHTML = '';

        let totalItems = 0;

        selectedCheckboxes.forEach(checkbox => {
            const dept = checkbox.dataset.department;
            const line = checkbox.dataset.line;
            const shift = checkbox.dataset.shift;
            const itemCount = parseInt(checkbox.dataset.itemCount || '0');

            totalItems += itemCount;

            const locationItem = document.createElement('div');
            locationItem.className = 'mb-2 p-2 border rounded bg-dark text-info';
            locationItem.innerHTML = `
                <div class="d-flex justify-content-between align-items-center">
                    <div>
                        <span class="fw-bold">${escapeHtml(dept)}/${escapeHtml(line)}/${escapeHtml(shift)}</span>
                        <span class="badge bg-secondary ms-2">${itemCount} items</span>
                    </div>
                    <button type="button" class="btn btn-sm btn-outline-danger remove-location-btn">
                        <i class="bi bi-x"></i>
                    </button>
                </div>
            `;

            // Add remove button handler
            locationItem.querySelector('.remove-location-btn').addEventListener('click', function () {
                checkbox.checked = false;
                updateSelectedLocations();
            });

            selectedLocationsList.appendChild(locationItem);
        });

        // Update total items count
        if (totalItemsCount) {
            totalItemsCount.textContent = totalItems;
        }
    }
}

/**
 * Save bulk weight for selected locations
 */
function saveBulkWeight() {
    // Get selected locations
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        showNotification('Please select at least one location', 'error');
        return;
    }

    // Get weight value
    const weightInput = document.getElementById('bulkWeightKg');
    const weight = parseFloat(weightInput?.value);

    if (!weight || weight <= 0) {
        showNotification('Please enter a valid weight greater than zero', 'error');
        return;
    }

    // Get description and token
    const description = document.getElementById('bulkWeightDescription')?.value || '';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showNotification('Security token not found', 'error');
        return;
    }

    // Build locations array
    const locations = [];
    selectedCheckboxes.forEach(cb => {
        // Ensure shift value doesn't exceed database limit
        let shift = cb.getAttribute('data-shift') || '';
        if (shift.length > 30) shift = shift.substring(0, 30);

        locations.push({
            Department: cb.getAttribute('data-department'),
            Line: cb.getAttribute('data-line'),
            Shift: shift
        });
    });

    // Update button state
    const saveBtn = document.getElementById('saveBulkWeightBtn');
    let originalText = '';

    if (saveBtn) {
        originalText = saveBtn.innerHTML;
        saveBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
        saveBtn.disabled = true;
    }

    // Submit bulk weight
    fetch('/ScrapApproval/SaveBulkWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Locations: locations,
            Weight: weight,
            Description: description || `Bulk weighing for ${locations.length} locations`
        })
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .then(data => {
            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            if (data.success) {
                // Show success notification
                showNotification('Weight saved successfully', 'success');

                // Reset form
                if (weightInput) weightInput.value = '';

                const descInput = document.getElementById('bulkWeightDescription');
                if (descInput) descInput.value = '';

                // Reset selections
                document.getElementById('selectAllLocations').checked = false;
                selectedCheckboxes.forEach(cb => {
                    cb.checked = false;
                });
                updateSelectedLocations();

                // Refresh data
                setTimeout(() => {
                    loadLocationsWithApprovedScrap();
                    loadWeightHistory();
                }, 500);
            } else {
                showNotification(data.message || 'Error saving weight', 'error');
            }
        })
        .catch(error => {
            console.error('Error saving bulk weight:', error);

            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            showNotification('Error saving weight. Please try again.', 'error');
        });
}

/**
 * Show weight prompt modal for TeamLeader
 */
function showWeightPrompt(scrapId, department, line, shift) {
    // Find the modal
    const modal = document.getElementById('weightUpdateModal');
    if (!modal) {
        console.error("Weight update modal not found");
        return;
    }

    // Set scrap ID in the form
    document.getElementById('weightScrapId').value = scrapId;

    // Store location info as data attributes if provided
    if (department && line && shift) {
        modal.setAttribute('data-department', department);
        modal.setAttribute('data-line', line);
        modal.setAttribute('data-shift', shift);
    }

    // Clear previous value and error
    document.getElementById('weightValue').value = '';
    const errorMessage = modal.querySelector('.error-message');
    if (errorMessage) errorMessage.style.display = 'none';

    // Show the modal
    modal.style.display = 'flex';

    // Focus on weight input
    setTimeout(() => {
        document.getElementById('weightValue').focus();
    }, 100);

    // Fix confirm button
    const confirmBtn = document.getElementById('confirmWeightBtn');
    if (confirmBtn) {
        const newBtn = confirmBtn.cloneNode(true);
        confirmBtn.parentNode.replaceChild(newBtn, confirmBtn);

        newBtn.addEventListener('click', function () {
            submitWeightUpdate();
        });
    }
}

/**
 * Submit weight update from modal
 */
function submitWeightUpdate() {
    const scrapId = document.getElementById('weightScrapId').value;
    const weight = parseFloat(document.getElementById('weightValue').value);
    const modal = document.getElementById('weightUpdateModal');

    if (!weight || weight <= 0) {
        const errorMessage = modal.querySelector('.error-message');
        if (errorMessage) {
            errorMessage.textContent = 'Please enter a valid weight greater than zero';
            errorMessage.style.display = 'block';
        }
        return;
    }

    // Get data from either modal attributes or data-attributes on the button
    const department = modal.getAttribute('data-department');
    const line = modal.getAttribute('data-line');
    const shift = modal.getAttribute('data-shift');

    // Get CSRF token
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    // Show loading state
    const confirmBtn = document.getElementById('confirmWeightBtn');
    const originalText = confirmBtn.innerHTML;
    confirmBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
    confirmBtn.disabled = true;

    // Submit weight using the AddWeight endpoint
    fetch('/ScrapApproval/AddWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapId: parseInt(scrapId),
            Department: department,
            Line: line,
            Shift: shift,
            Weight: weight
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            confirmBtn.innerHTML = originalText;
            confirmBtn.disabled = false;

            if (data.success) {
                // Close modal
                hideModal('weightUpdateModal');

                // Show success
                showNotification('Weight saved successfully', 'success');

                // Refresh data after successful save
                if (typeof loadWeightHistory === 'function') {
                    loadWeightHistory();
                }
            } else {
                // Show error
                const errorMessage = modal.querySelector('.error-message');
                if (errorMessage) {
                    errorMessage.textContent = data.message || 'Error saving weight';
                    errorMessage.style.display = 'block';
                } else {
                    showNotification(data.message || 'Error saving weight', 'error');
                }
            }
        })
        .catch(error => {
            console.error('Error saving weight:', error);

            // Reset button state
            confirmBtn.innerHTML = originalText;
            confirmBtn.disabled = false;

            // Show error
            const errorMessage = modal.querySelector('.error-message');
            if (errorMessage) {
                errorMessage.textContent = 'Error saving weight. Please try again.';
                errorMessage.style.display = 'block';
            } else {
                showNotification('Error saving weight. Please try again.', 'error');
            }
        });
}

/**
 * Load weight history for current location
 */
function loadWeightHistory() {
    // Get current location
    const department = document.querySelector('.location-value[data-department]')?.getAttribute('data-department');
    const line = document.querySelector('.location-value[data-line]')?.getAttribute('data-line');
    const shift = document.querySelector('.location-value[data-shift]')?.getAttribute('data-shift');

    if (!department || !line || !shift) {
        console.log("Missing location information for weight history");
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    fetch(`/ScrapApproval/GetWeightHistory?department=${encodeURIComponent(department)}&line=${encodeURIComponent(line)}&shift=${encodeURIComponent(shift)}`, {
        headers: {
            'RequestVerificationToken': token
        }
    })
        .then(response => response.json())
        .then(data => {
            const historyBody = document.getElementById('weightHistoryTableBody');
            const emptyState = document.getElementById('weightHistoryEmpty');

            if (!historyBody) return;

            if (data.success && data.history && data.history.length > 0) {
                historyBody.innerHTML = '';

                data.history.forEach(item => {
                    const locationDisplay = item.IsBulkWeighing ?
                        item.Locations :
                        `${item.Department}/${item.Line}/${item.Shift}`;

                    const row = document.createElement('tr');
                    row.innerHTML = `
                    <td>${escapeHtml(locationDisplay)}</td>
                    <td>${item.Weight} kg</td>
                    <td>${escapeHtml(item.Description || '')}</td>
                    <td>${item.CreatedAt}</td>
                    <td>${escapeHtml(item.CreatedBy)}</td>
                `;
                    historyBody.appendChild(row);
                });

                if (emptyState) emptyState.style.display = 'none';
            } else {
                historyBody.innerHTML = '<tr><td colspan="5" class="text-center">No weight history found for this location.</td></tr>';
                if (emptyState) emptyState.style.display = 'block';
            }
        })
        .catch(error => {
            console.error('Error loading weight history:', error);

            const historyBody = document.getElementById('weightHistoryTableBody');
            if (historyBody) {
                historyBody.innerHTML = '<tr><td colspan="5" class="text-center text-danger">Error loading weight history</td></tr>';
            }
        });
}

/**
 * Populate location form with current values
 */
function populateLocationForm() {
    const currentDept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const currentLine = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const currentShift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    // Set department
    const deptSelect = document.getElementById('updateDepartment');
    if (deptSelect && currentDept) {
        for (let i = 0; i < deptSelect.options.length; i++) {
            if (deptSelect.options[i].value === currentDept ||
                deptSelect.options[i].text === currentDept) {
                deptSelect.selectedIndex = i;
                break;
            }
        }

        // Trigger change event to populate lines
        const event = new Event('change');
        deptSelect.dispatchEvent(event);
    }

    // Set line with delay (to allow for lines to load)
    setTimeout(function () {
        const lineSelect = document.getElementById('updateLine');
        if (lineSelect && currentLine) {
            for (let i = 0; i < lineSelect.options.length; i++) {
                if (lineSelect.options[i].value === currentLine ||
                    lineSelect.options[i].text === currentLine) {
                    lineSelect.selectedIndex = i;
                    break;
                }
            }
        }

        // Set shift
        const shiftSelect = document.getElementById('updateShift');
        if (shiftSelect && currentShift) {
            for (let i = 0; i < shiftSelect.options.length; i++) {
                if (shiftSelect.options[i].value === currentShift ||
                    shiftSelect.options[i].text === currentShift) {
                    shiftSelect.selectedIndex = i;
                    break;
                }
            }
        }
    }, 300);
}

/**
 * Populate line options based on selected department
 */
function populateLineOptions() {
    const department = document.getElementById('updateDepartment').value;
    const lineSelect = document.getElementById('updateLine');

    // Clear existing options
    while (lineSelect.options.length > 1) {
        lineSelect.remove(1);
    }

    // Exit if no department selected
    if (!department) {
        lineSelect.options[0].text = "Select a department first";
        return;
    }

    // Set first option text
    lineSelect.options[0].text = "Select Line";

    // Get line options from hidden data
    const lineOptions = document.querySelectorAll(`#line-options-data option[data-dept="${department}"]`);

    // Add matching options
    lineOptions.forEach(function (option) {
        var newOption = document.createElement('option');
        newOption.value = option.value;
        newOption.text = option.text || option.value;
        lineSelect.add(newOption);
    });

    if (lineOptions.length === 0) {
        lineSelect.options[0].text = "No lines found";
    }
}

/**
 * Submit location form
 */
function submitLocationForm() {
    // Get form values
    const dept = document.getElementById('updateDepartment')?.value;
    const line = document.getElementById('updateLine')?.value;
    const shift = document.getElementById('updateShift')?.value;

    // Validate form
    if (!dept || !line || !shift) {
        showErrorMessage('locationError', "All fields are required");
        return;
    }

    // Show loading state
    const submitBtn = document.getElementById('submitLocationBtn');
    const btnText = submitBtn.querySelector('.button-text');
    const spinner = submitBtn.querySelector('.spinner');

    if (btnText) btnText.style.display = 'none';
    if (spinner) spinner.style.display = 'inline-block';
    submitBtn.disabled = true;

    // Get token from hidden input
    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    // Create a form element
    const form = document.createElement('form');
    form.method = 'post';
    form.action = '/ScrapApproval/UpdateLocationForm';
    form.style.display = 'none';

    // Add fields
    const addField = (name, value) => {
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = name;
        input.value = value;
        form.appendChild(input);
    };

    addField('Department', dept);
    addField('Line', line);
    addField('Shift', shift);
    addField('__RequestVerificationToken', token);

    // Add form to document, submit it, then remove it
    document.body.appendChild(form);
    form.submit();

    // Hide modal to prevent double submission
    document.getElementById('locationModal').style.display = 'none';
}

/**
 * Show notification toast
 */
function showNotification(message, type = 'success') {
    const toast = document.getElementById('notificationToast');
    const toastBody = document.getElementById('toastBody');

    if (toast && toastBody) {
        // Set content
        toastBody.textContent = message;

        // Set type
        toast.className = toast.className.replace(/bg-\w+/, '');
        toast.classList.add(type === 'error' ? 'bg-danger' :
            type === 'warning' ? 'bg-warning' : 'bg-success');

        // Check if we have the Bootstrap Toast component
        if (typeof bootstrap !== 'undefined' && bootstrap.Toast) {
            const bsToast = new bootstrap.Toast(toast);
            bsToast.show();
        } else {
            // Fallback if Bootstrap is not available
            toast.style.display = 'block';
            setTimeout(() => { toast.style.display = 'none'; }, 5000);
        }
    } else {
        // Fallback to alert
        alert(message);
    }
}

/**
 * Show error message in a form or container
 */
function showErrorMessage(elementId, message) {
    const errorElement = document.getElementById(elementId);
    if (errorElement) {
        errorElement.textContent = message;
        errorElement.style.display = 'block';
    } else {
        showNotification(message, 'error');
    }
}

/**
 * Hide a modal by ID
 */
function hideModal(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        modal.style.display = 'none';
    }
}

/**
 * Setup notification system
 */
function setupNotifications() {
    // Create toast container if not exists
    if (!document.getElementById('notificationToast')) {
        const toastContainer = document.createElement('div');
        toastContainer.className = 'position-fixed bottom-0 end-0 p-3';
        toastContainer.style.zIndex = '11';

        toastContainer.innerHTML = `
            <div id="notificationToast" class="toast align-items-center text-white bg-success" role="alert" aria-live="assertive" aria-atomic="true">
                <div class="d-flex">
                    <div id="toastBody" class="toast-body">
                        Weight saved successfully!
                    </div>
                    <button type="button" class="btn-close btn-close-white me-2 m-auto" data-bs-dismiss="toast" aria-label="Close"></button>
                </div>
            </div>
        `;

        document.body.appendChild(toastContainer);
    }
}

/**
 * Escape HTML special characters to prevent XSS
 */
function escapeHtml(text) {
    if (!text) return '';

    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}

// Make helper functions globally available
window.showWeightPrompt = showWeightPrompt;
window.toggleAllLocations = toggleAllLocations;
window.updateSelectedLocations = updateSelectedLocations;
window.selectApprovedLocations = selectApprovedLocations;
window.hideModal = hideModal;
window.populateLineOptions = populateLineOptions;
window.submitLocationForm = submitLocationForm;
window.submitWeightUpdate = submitWeightUpdate;
window.saveTeamLeaderWeight = saveTeamLeaderWeight;