/**
 * Enhanced Dashboard Fixes
 * Comprehensive solution to fix button responsiveness issues and extend functionality
 * for the Scrap Approval Dashboard.
 */

// Wait for DOM to be fully loaded
document.addEventListener('DOMContentLoaded', function () {
    console.log("Dashboard fixes loading...");

    // Fix all button event listeners
    fixAllButtons();

    // Apply other fixes
    fixModalButtons();
    fixLocationUpdate();
    updateLocationStats();
    fixWeightManagement();
    setupBulkApprovalHandling();

    // Load scrap items for the current location
    loadScrapItems();

    // Listen for location changes
    listenForLocationChanges();

    console.log("Dashboard enhancements applied successfully");
});

/**
 * Fix all buttons by ensuring proper event handlers and accessibility
 */
function fixAllButtons() {
    // Add this missing function that was referenced in the original code
    console.log("Fixing all buttons...");

    // General fix for all buttons with specific classes
    document.querySelectorAll('.btn-cyber, .action-btn').forEach(button => {
        // Remove existing event listeners (to prevent duplicates)
        const newBtn = button.cloneNode(true);
        button.parentNode.replaceChild(newBtn, button);

        // Add proper tabindex for accessibility
        if (!newBtn.hasAttribute('tabindex')) {
            newBtn.setAttribute('tabindex', '0');
        }

        // Add both click and keyboard handlers
        newBtn.addEventListener('click', function (e) {
            // Prevent default action to avoid any form submission issues
            e.preventDefault();

            // Get the button's intended action from data attributes
            const action = this.getAttribute('data-action') || '';
            const id = this.getAttribute('data-id') || '';

            // Handle different action types
            if (this.classList.contains('approve')) {
                handleApproval(id, true);
            } else if (this.classList.contains('reject')) {
                handleApproval(id, false);
            } else if (this.classList.contains('add-weight')) {
                const scrapId = this.getAttribute('data-scrap-id');
                showWeightPrompt(scrapId);
            } else if (action === 'update-location') {
                document.getElementById('locationModal').style.display = 'flex';
            } else {
                // Handle generic button click by looking for the original onclick function
                const onclickAttr = this.getAttribute('onclick');
                if (onclickAttr) {
                    // Try to execute the onclick function
                    try {
                        new Function(onclickAttr)();
                    } catch (error) {
                        console.error('Error executing button action:', error);
                    }
                }
            }
        });

        // Add keyboard support for accessibility
        newBtn.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                this.click();
            }
        });
    });

    // Special fix for select all checkbox
    const selectAllCheckbox = document.getElementById('selectAll');
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', function () {
            toggleAllRows();
        });
    }

    // Fix specifically for the user management buttons
    const newUserButton = document.getElementById('newUserButton');
    if (newUserButton) {
        newUserButton.onclick = function () {
            document.getElementById('newUserModal').style.display = 'flex';
        };
    }

    const resetPasswordButton = document.getElementById('resetPasswordButton');
    if (resetPasswordButton) {
        resetPasswordButton.onclick = function () {
            document.getElementById('resetPasswordModal').style.display = 'flex';
        };
    }

    const updateLocationBtn = document.getElementById('updateLocationBtn');
    if (updateLocationBtn) {
        updateLocationBtn.onclick = function () {
            document.getElementById('locationModal').style.display = 'flex';
        };
    }

    console.log("All buttons fixed!");
}

/**
 * Fix modal buttons specifically
 */
function fixModalButtons() {
    console.log("Fixing modal buttons...");

    // Close buttons for modals
    document.querySelectorAll('.btn-close-cyber, .modal-footer .btn-cyber-red').forEach(btn => {
        btn.onclick = function () {
            const modal = this.closest('.modal-cyber');
            if (modal) modal.style.display = 'none';
        };
    });

    // Submit buttons for modals
    document.querySelectorAll('.modal-footer .btn-cyber-green[type="submit"]').forEach(btn => {
        btn.onclick = function () {
            const form = this.closest('form');
            if (form) {
                form.submit();
            }
        };
    });

    // Fix submit location button
    const submitLocationBtn = document.getElementById('submitLocationBtn');
    if (submitLocationBtn) {
        submitLocationBtn.onclick = function () {
            submitLocationUpdate();
        };
    }

    // Fix confirm weight button
    const confirmWeightBtn = document.getElementById('confirmWeightBtn');
    if (confirmWeightBtn) {
        confirmWeightBtn.onclick = function () {
            submitWeightUpdate();
        };
    }

    // Fix confirm reject button
    const confirmRejectBtn = document.getElementById('confirmRejectBtn');
    if (confirmRejectBtn) {
        confirmRejectBtn.onclick = function () {
            submitRejection();
        };
    }

    // Add CSS to ensure modals appear above other content
    const style = document.createElement('style');
    style.textContent = `
    .modal-cyber {
      z-index: 9999 !important;
      display: none;
      position: fixed;
      top: 0;
      left: 0;
      width: 100%;
      height: 100%;
      background-color: rgba(0, 0, 0, 0.5);
      align-items: center;
      justify-content: center;
    }
    .modal-cyber .modal-dialog {
      z-index: 10000 !important;
      background-color: #0f1923;
      border: 1px solid #00ffd5;
      border-radius: 5px;
      width: 90%;
      max-width: 600px;
      max-height: 90vh;
      overflow-y: auto;
    }
    .modal-cyber .modal-header,
    .modal-cyber .modal-footer {
      padding: 15px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      border-bottom: 1px solid #203b4c;
    }
    .modal-cyber .modal-body {
      padding: 20px;
    }
    .btn-cyber {
      cursor: pointer;
    }
  `;
    document.head.appendChild(style);

    console.log("Modal buttons fixed!");
}

/**
 * Fix location update functionality
 */
function fixLocationUpdate() {
    console.log("Fixing location update...");

    const deptSelect = document.getElementById('updateDepartment');
    const lineSelect = document.getElementById('updateLine');
    const shiftSelect = document.getElementById('updateShift');

    if (deptSelect) {
        // Ensure department change populates lines
        deptSelect.onchange = function () {
            const department = this.value;

            // Clear existing options
            if (lineSelect) {
                while (lineSelect.options.length > 1) {
                    lineSelect.remove(1);
                }

                // Get line options for this department
                const lineOptions = document.querySelectorAll(`#line-options-data option[data-dept="${department}"]`);
                lineOptions.forEach(option => {
                    const newOption = document.createElement('option');
                    newOption.value = option.value;
                    newOption.text = option.textContent;
                    lineSelect.add(newOption);
                });
            }
        };
    }

    // Fix for submitting the location update
    function submitLocationUpdate() {
        console.log("Submitting location update...");

        const department = deptSelect ? deptSelect.value : '';
        const line = lineSelect ? lineSelect.value : '';
        const shift = shiftSelect ? shiftSelect.value : '';

        if (!department || !line || !shift) {
            showErrorMessage('locationError', 'All fields are required');
            return;
        }

        // Show loading state
        const submitBtn = document.getElementById('submitLocationBtn');
        if (submitBtn) {
            const buttonText = submitBtn.querySelector('.button-text');
            const spinner = submitBtn.querySelector('.spinner');

            if (buttonText) buttonText.style.display = 'none';
            if (spinner) spinner.style.display = 'inline-block';
            submitBtn.disabled = true;
        }

        // Get CSRF token
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        // Send location update via AJAX
        fetch('/ScrapApproval/UpdateLocation', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify({
                Department: department,
                Line: line,
                Shift: shift
            })
        })
            .then(response => response.json())
            .then(data => {
                // Reset button state
                if (submitBtn) {
                    const buttonText = submitBtn.querySelector('.button-text');
                    const spinner = submitBtn.querySelector('.spinner');

                    if (buttonText) buttonText.style.display = 'inline-block';
                    if (spinner) spinner.style.display = 'none';
                    submitBtn.disabled = false;
                }

                if (data.success) {
                    // Update location display
                    const deptDisplay = document.querySelector('.location-value[data-department]');
                    const lineDisplay = document.querySelector('.location-value[data-line]');
                    const shiftDisplay = document.querySelector('.location-value[data-shift]');

                    if (deptDisplay) {
                        deptDisplay.textContent = department;
                        deptDisplay.setAttribute('data-department', department);
                    }

                    if (lineDisplay) {
                        lineDisplay.textContent = line;
                        lineDisplay.setAttribute('data-line', line);
                    }

                    if (shiftDisplay) {
                        shiftDisplay.textContent = shift;
                        shiftDisplay.setAttribute('data-shift', shift);
                    }

                    // Close modal
                    const modal = document.getElementById('locationModal');
                    if (modal) modal.style.display = 'none';

                    // Show success message
                    showNotification('Location Updated', 'Your location has been updated successfully', 'success');

                    // Reload data for new location
                    updateLocationStats();
                    loadScrapItems();
                    loadWeightHistory();
                } else {
                    // Show error
                    showErrorMessage('locationError', data.message || 'Error updating location');
                }
            })
            .catch(error => {
                console.error('Error updating location:', error);

                // Reset button state
                if (submitBtn) {
                    const buttonText = submitBtn.querySelector('.button-text');
                    const spinner = submitBtn.querySelector('.spinner');

                    if (buttonText) buttonText.style.display = 'inline-block';
                    if (spinner) spinner.style.display = 'none';
                    submitBtn.disabled = false;
                }

                showErrorMessage('locationError', 'Error updating location. Please try again.');
            });
    }

    // Make function available globally
    window.submitLocationUpdate = submitLocationUpdate;

    console.log("Location update fixed!");
}

/**
 * Update location stats display
 */
function updateLocationStats() {
    console.log("Updating location stats...");

    // Get current location
    const department = document.querySelector('.location-value[data-department]')?.getAttribute('data-department');
    const line = document.querySelector('.location-value[data-line]')?.getAttribute('data-line');
    const shift = document.querySelector('.location-value[data-shift]')?.getAttribute('data-shift');

    if (!department || !line || !shift) {
        console.error("Could not find current location information");
        return;
    }

    // Display current location at the top of weight management section
    const currentLocationElement = document.getElementById('current-location');
    if (currentLocationElement) {
        currentLocationElement.textContent = `${department}/${line}/${shift}`;
    }

    // Also update hidden fields used by the system
    const currentDeptField = document.getElementById('currentDepartment');
    const currentLineField = document.getElementById('currentLine');
    const currentShiftField = document.getElementById('currentShift');

    if (currentDeptField) currentDeptField.value = department;
    if (currentLineField) currentLineField.value = line;
    if (currentShiftField) currentShiftField.value = shift;

    // Get stats via AJAX for the current location
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Check if URL needs encoding
    const encodedDept = encodeURIComponent(department);
    const encodedLine = encodeURIComponent(line);
    const encodedShift = encodeURIComponent(shift);

    fetch(`/ScrapApproval/GetFilteredScrapData`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Department: department,
            Line: line,
            Shift: shift,
            TimeFilter: 'all'
        })
    })
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                // Update stats display
                const statValues = document.querySelectorAll('.stat-value');

                // Total Items
                if (statValues[0]) {
                    statValues[0].textContent = data.stats.totalCount || '0';
                }

                // Total Cost
                if (statValues[1]) {
                    const costFormatted = (data.stats.totalCost || 0).toFixed(2);
                    statValues[1].textContent = `R ${costFormatted}`;
                }

                // Pending Approvals
                if (statValues[2]) {
                    statValues[2].textContent = data.stats.pendingCount || '0';
                }
            } else {
                console.error("Error fetching stats data:", data.message);
            }
        })
        .catch(error => {
            console.error('Error fetching stats:', error);
        });

    console.log("Location stats update triggered");
}

/**
 * Fix weight management functionality
 */
function fixWeightManagement() {
    console.log("Fixing weight management...");

    // Get user type to determine which weight features to enable
    const metaUserType = document.querySelector('meta[name="user-type"]');
    let userType = '';

    if (metaUserType) {
        userType = metaUserType.content.toLowerCase();
    } else {
        // Try to determine user type from the view bag
        if (document.querySelector('.add-weight')) {
            userType = 'teamleader';
        } else if (document.getElementById('filterDepartment')) {
            userType = 'admin';
        }
    }

    console.log(`Detected user type: ${userType}`);

    // Fix weight history loading
    loadWeightHistory();

    // For admin - enable bulk weighing
    if (userType === 'admin') {
        // Fix locations loading for bulk weighing
        loadLocationsWithApprovedScrap();

        // Fix filter button
        const filterBtn = document.getElementById('applyFilterBtn');
        if (filterBtn) {
            filterBtn.onclick = function () {
                const department = document.getElementById('filterDepartment')?.value || 'All Departments';
                loadLocationsWithApprovedScrap(department);
            };
        }

        // Fix bulk save button
        const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');
        if (saveBulkWeightBtn) {
            saveBulkWeightBtn.onclick = function () {
                saveBulkWeight();
            };
        }

        // Fix checkbox selection
        const selectAllCheckbox = document.getElementById('selectAllLocations');
        if (selectAllCheckbox) {
            selectAllCheckbox.onclick = function () {
                toggleAllLocations();
            };
        }

        // Fix clear selections button
        const clearSelectionsBtn = document.getElementById('clearSelectionsBtn');
        if (clearSelectionsBtn) {
            clearSelectionsBtn.onclick = function () {
                document.getElementById('selectAllLocations').checked = false;
                document.querySelectorAll('.location-checkbox').forEach(cb => {
                    cb.checked = false;
                });
                updateSelectedLocations();
            };
        }

        // Auto-select toggle
        const autoSelectToggle = document.getElementById('autoSelectToggle');
        if (autoSelectToggle) {
            autoSelectToggle.addEventListener('change', function () {
                if (this.checked) {
                    autoSelectApprovedLocations();
                } else {
                    document.querySelectorAll('.location-checkbox').forEach(cb => {
                        cb.checked = false;
                    });
                    updateSelectedLocations();
                }
            });
        }

        // Select all approved button
        const selectAllApprovedBtn = document.getElementById('selectAllApprovedBtn');
        if (selectAllApprovedBtn) {
            selectAllApprovedBtn.onclick = function () {
                autoSelectApprovedLocations();
            };
        }
    }

    // For TeamLeader - enable single location weighing
    if (userType === 'teamleader') {
        const addWeightBtns = document.querySelectorAll('.add-weight');
        addWeightBtns.forEach(btn => {
            btn.onclick = function () {
                const scrapId = this.getAttribute('data-scrap-id');
                showWeightPrompt(scrapId);
            };
        });

        // Fix weight update modal
        const confirmWeightBtn = document.getElementById('confirmWeightBtn');
        if (confirmWeightBtn) {
            confirmWeightBtn.onclick = function () {
                submitWeightUpdate();
            };
        }
    }

    console.log("Weight management fixed!");
}

/**
 * Load weight history for current location
 */
function loadWeightHistory() {
    console.log("Loading weight history...");

    const department = document.querySelector('.location-value[data-department]')?.getAttribute('data-department');
    const line = document.querySelector('.location-value[data-line]')?.getAttribute('data-line');
    const shift = document.querySelector('.location-value[data-shift]')?.getAttribute('data-shift');

    if (!department || !line || !shift) {
        console.error("Missing location information for weight history");
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    fetch(`/ScrapApproval/GetWeightHistory?department=${encodeURIComponent(department)}&line=${encodeURIComponent(line)}&shift=${encodeURIComponent(shift)}`, {
        headers: {
            'RequestVerificationToken': token
        }
    })
        .then(response => response.json())
        .then(data => {
            const historyBody = document.getElementById('weightHistoryTableBody');
            const emptyState = document.getElementById('weightHistoryEmpty');

            if (!historyBody) {
                console.error("Weight history table body not found");
                return;
            }

            if (data.success && data.history && data.history.length > 0) {
                historyBody.innerHTML = '';

                data.history.forEach(item => {
                    const locationDisplay = item.IsBulkWeighing ?
                        item.Locations :
                        `${item.Department}/${item.Line}/${item.Shift}`;

                    const row = document.createElement('tr');
                    row.innerHTML = `
          <td>${locationDisplay}</td>
          <td>${item.Weight} kg</td>
          <td>${item.Description || ''}</td>
          <td>${item.CreatedAt}</td>
          <td>${item.CreatedBy}</td>
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
                historyBody.innerHTML = '<tr><td colspan="5" class="text-center text-danger">Error loading weight history. Please try again.</td></tr>';
            }
        });
}

/**
 * Load locations with approved scrap items
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

    // Build API URL
    let url = '/ScrapApproval/GetLocationsWithApprovedScrap';
    if (department && department !== 'All Departments') {
        url += `?department=${encodeURIComponent(department)}`;
    }

    // Make request
    fetch(url, {
        headers: {
            'RequestVerificationToken': token
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
                tableBody.innerHTML = '';

                data.locations.forEach(loc => {
                    // Calculate CSS class based on unweighed items
                    const rowClass = loc.unweighedScrapCount > 0 ? 'table-success' : '';

                    const row = document.createElement('tr');
                    if (rowClass) row.className = rowClass;

                    row.innerHTML = `
          <td>
            <input type="checkbox" class="location-checkbox" 
                data-department="${loc.department}" 
                data-line="${loc.line}" 
                data-shift="${loc.shift}"
                data-item-count="${loc.approvedItemCount}"
                data-unweighed="${loc.unweighedScrapCount}"
                data-should-select="${loc.shouldAutoSelect ? 'true' : 'false'}"
                onchange="updateSelectedLocations()">
          </td>
          <td>${loc.department}</td>
          <td>${loc.line}</td>
          <td>${loc.shift}</td>
          <td>${loc.approvedItemCount}</td>
          <td>${loc.lastWeighedDate || 'Never'}</td>
        `;
                    tableBody.appendChild(row);
                });

                if (tableElement) tableElement.style.display = 'block';

                // Auto-select locations with unweighed items if toggle is checked
                const autoSelectToggle = document.getElementById('autoSelectToggle');
                if (autoSelectToggle && autoSelectToggle.checked) {
                    autoSelectApprovedLocations();
                }
            } else {
                tableBody.innerHTML = '';
                if (emptyState) emptyState.style.display = 'block';
            }
        })
        .catch(error => {
            console.error('Error loading locations:', error);

            if (loadingElement) loadingElement.style.display = 'none';
            if (emptyState) {
                emptyState.style.display = 'block';
                emptyState.innerHTML = '<p>Error loading locations with approved scrap. Please try again.</p>';
            }
        });
}

/**
 * Auto-select locations with approved items
 */
function autoSelectApprovedLocations() {
    const locationCheckboxes = document.querySelectorAll('.location-checkbox');

    locationCheckboxes.forEach(cb => {
        // Auto-select if it has unweighed items or should be selected based on the server's recommendation
        const unweighedCount = parseInt(cb.getAttribute('data-unweighed') || '0');
        const shouldSelect = cb.getAttribute('data-should-select') === 'true';

        if (unweighedCount > 0 || shouldSelect) {
            cb.checked = true;
        } else {
            cb.checked = false;
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

    if (selectAllCheckbox) {
        locationCheckboxes.forEach(cb => {
            cb.checked = selectAllCheckbox.checked;
        });

        updateSelectedLocations();
    }
}

/**
 * Update selected locations display
 */
function updateSelectedLocations() {
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');
    const selectedContainer = document.getElementById('selectedLocationsContainer');
    const noLocationsSelected = document.getElementById('noLocationsSelected');
    const selectedLocationsList = document.getElementById('selectedLocationsList');
    const selectedLocationsCount = document.getElementById('selectedLocationsCount');
    const totalItemsCount = document.getElementById('totalItemsCount');
    const saveBulkWeightBtn = document.getElementById('saveBulkWeightBtn');

    if (selectedCheckboxes.length === 0) {
        if (selectedContainer) selectedContainer.style.display = 'none';
        if (noLocationsSelected) noLocationsSelected.style.display = 'block';
        if (saveBulkWeightBtn) {
            saveBulkWeightBtn.disabled = true;
            saveBulkWeightBtn.classList.add('disabled');
        }
        return;
    }

    if (selectedContainer) selectedContainer.style.display = 'block';
    if (noLocationsSelected) noLocationsSelected.style.display = 'none';
    if (saveBulkWeightBtn) {
        saveBulkWeightBtn.disabled = false;
        saveBulkWeightBtn.classList.remove('disabled');
    }

    if (selectedLocationsCount) {
        selectedLocationsCount.textContent = selectedCheckboxes.length;
    }

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
            locationItem.className = 'location-item mb-2 p-2 border rounded bg-dark text-cyan';
            locationItem.innerHTML = `
        <div class="d-flex justify-content-between align-items-center">
          <div>
            <span class="location-text">${dept}/${line}/${shift}</span>
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

        if (totalItemsCount) {
            totalItemsCount.textContent = totalItems;
        }
    }
}

/**
 * Save bulk weight for selected locations
 */
function saveBulkWeight() {
    const selectedCheckboxes = document.querySelectorAll('.location-checkbox:checked');
    if (selectedCheckboxes.length === 0) {
        showNotification('Error', 'Please select at least one location', 'danger');
        return;
    }

    const weight = parseFloat(document.getElementById('bulkWeightKg')?.value);
    if (!weight || weight <= 0) {
        showNotification('Error', 'Please enter a valid weight greater than zero', 'danger');
        return;
    }

    const description = document.getElementById('bulkWeightDescription')?.value || '';
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Build locations array
    const locations = Array.from(selectedCheckboxes).map(cb => ({
        Department: cb.dataset.department,
        Line: cb.dataset.line,
        Shift: cb.dataset.shift
    }));

    // Update button state
    const saveBtn = document.getElementById('saveBulkWeightBtn');
    let originalText = '';

    if (saveBtn) {
        originalText = saveBtn.innerHTML;
        saveBtn.innerHTML = '<i class="fas fa-spinner fa-spin"></i> Saving...';
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
            Description: description
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            if (data.success) {
                // Show success
                showNotification('Success', 'Weight saved successfully', 'success');

                // Reset form
                if (document.getElementById('bulkWeightKg')) {
                    document.getElementById('bulkWeightKg').value = '';
                }

                if (document.getElementById('bulkWeightDescription')) {
                    document.getElementById('bulkWeightDescription').value = '';
                }

                // Uncheck all locations
                if (document.getElementById('selectAllLocations')) {
                    document.getElementById('selectAllLocations').checked = false;
                }

                selectedCheckboxes.forEach(cb => {
                    cb.checked = false;
                });

                updateSelectedLocations();

                // Reload weight history
                loadWeightHistory();

                // Reload locations to reflect the new weighing
                const department = document.getElementById('filterDepartment')?.value || 'All Departments';
                loadLocationsWithApprovedScrap(department);
            } else {
                // Show error
                showNotification('Error', data.message || 'Error saving weight', 'danger');
            }
        })
        .catch(error => {
            console.error('Error:', error);

            // Reset button state
            if (saveBtn) {
                saveBtn.innerHTML = originalText;
                saveBtn.disabled = false;
            }

            // Show error
            showNotification('Error', 'An error occurred while saving weight records', 'danger');
        });
}

/**
 * Show weight prompt for an individual scrap item
 */
function showWeightPrompt(scrapId) {
    console.log(`Showing weight prompt for scrap ID: ${scrapId}`);

    // Find the modal
    const modal = document.getElementById('weightUpdateModal');
    if (!modal) {
        console.error("Weight update modal not found");
        return;
    }

    // Set scrap ID in the form
    const scrapIdField = document.getElementById('weightScrapId');
    if (scrapIdField) {
        scrapIdField.value = scrapId;
    }

    // Clear previous values
    const weightField = document.getElementById('weightValue');
    if (weightField) {
        weightField.value = '';
    }

    // Clear error message
    const errorMessage = modal.querySelector('.error-message');
    if (errorMessage) {
        errorMessage.style.display = 'none';
        errorMessage.textContent = '';
    }

    // Show the modal
    modal.style.display = 'flex';
}

/**
 * Submit weight update for an individual item
 */
function submitWeightUpdate() {
    console.log("Submitting weight update...");

    const scrapId = document.getElementById('weightScrapId')?.value;
    const weight = parseFloat(document.getElementById('weightValue')?.value);

    if (!scrapId) {
        showErrorMessage('weightUpdateForm .error-message', 'Missing scrap ID');
        return;
    }

    if (!weight || weight <= 0) {
        showErrorMessage('weightUpdateForm .error-message', 'Please enter a valid weight greater than zero');
        return;
    }

    // Get token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Update button state
    const confirmBtn = document.getElementById('confirmWeightBtn');
    let originalText = '';

    if (confirmBtn) {
        originalText = confirmBtn.innerHTML;
        confirmBtn.innerHTML = '<span class="spinner"><i class="bi bi-arrow-repeat"></i></span>';
        confirmBtn.disabled = true;
    }

    // Submit weight
    fetch('/ScrapApproval/AddWeight', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapId: parseInt(scrapId),
            Weight: weight
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            if (confirmBtn) {
                confirmBtn.innerHTML = originalText;
                confirmBtn.disabled = false;
            }

            if (data.success) {
                // Close modal
                hideModal('weightUpdateModal');

                // Show success
                showNotification('Success', 'Weight saved successfully', 'success');

                // Reload weight history
                loadWeightHistory();

                // Reload scrap items to reflect the weight update
                loadScrapItems();
            } else {
                // Show error
                showErrorMessage('weightUpdateForm .error-message', data.message || 'Error saving weight');
            }
        })
        .catch(error => {
            console.error('Error:', error);

            // Reset button state
            if (confirmBtn) {
                confirmBtn.innerHTML = originalText;
                confirmBtn.disabled = false;
            }

            // Show error
            showErrorMessage('weightUpdateForm .error-message', 'An error occurred while saving weight');
        });
}

/**
 * Load scrap items for current location
 */
function loadScrapItems() {
    console.log("Loading scrap items...");

    const department = document.querySelector('.location-value[data-department]')?.getAttribute('data-department');
    const line = document.querySelector('.location-value[data-line]')?.getAttribute('data-line');
    const shift = document.querySelector('.location-value[data-shift]')?.getAttribute('data-shift');

    if (!department || !line || !shift) {
        console.error("Missing location information for scrap items");
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Show loading in the table
    const scrapTable = document.querySelector('.cyber-table tbody');
    if (!scrapTable) {
        console.error("Scrap items table not found");
        return;
    }

    scrapTable.innerHTML = '<tr><td colspan="9" class="text-center">Loading scrap items...</td></tr>';

    // Get scrap items for this location
    fetch('/ScrapApproval/GetFilteredScrapData', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            Department: department,
            Line: line,
            Shift: shift,
            Filter: 'all',
            TimeFilter: 'all'
        })
    })
        .then(response => response.json())
        .then(data => {
            if (data.success && data.data && data.data.length > 0) {
                scrapTable.innerHTML = '';

                data.data.forEach(item => {
                    const statusClass = item.Status === 'Approved' ? 'approved' :
                        item.Status === 'Rejected' ? 'rejected' : 'pending';

                    // Determine if current user can approve this item
                    const canApprove = document.getElementById('bulk-approve-btn') !== null;

                    // Determine if current user can add weight
                    const canAddWeight = document.querySelector('.add-weight') !== null;

                    let row = `
          <tr>
            <td>
              <input type="checkbox" class="row-checkbox" data-id="${item.ScrapId}">
            </td>
            <td>${item.PartNumber}</td>
            <td>${item.Quantity}</td>
            <td>R ${parseFloat(item.MaterialCost).toFixed(2)}</td>
            <td>R ${parseFloat(item.TotalCost).toFixed(2)}</td>
            <td>
              <span class="status-badge ${statusClass}">${item.Status || 'Pending'}</span>
            </td>
            <td>
              <div class="approval-levels">
        `;

                    // Build approval level indicators - get current level
                    const currentLevel = item.CurrentApprovalLevel || 0;

                    for (let i = 1; i <= 4; i++) {
                        let levelStatus = 'disabled';

                        if (item.Status === 'Rejected') {
                            levelStatus = 'rejected';
                        } else {
                            // Check specific level status
                            let approvalStatus = null;
                            if (i === 1) approvalStatus = item.TeamLeaderApprovalStatus;
                            else if (i === 2) approvalStatus = item.GroupLeaderApprovalStatus;
                            else if (i === 3) approvalStatus = item.ManagerApprovalStatus;
                            else if (i === 4) approvalStatus = item.SeniorManagerApprovalStatus;

                            if (approvalStatus === 'Approved') levelStatus = 'approved';
                            else if (approvalStatus === 'Rejected') levelStatus = 'rejected';
                            else if (i === currentLevel + 1) levelStatus = 'pending';
                        }

                        const levelTooltip = i === 1 ? 'Team Leader' :
                            i === 2 ? 'Group Leader' :
                                i === 3 ? 'Manager' : 'Senior Manager';

                        row += `<div class="approval-level ${levelStatus}" data-tooltip="${levelTooltip}: ${levelStatus === 'approved' ? 'Approved' : levelStatus === 'rejected' ? 'Rejected' : levelStatus === 'pending' ? 'Pending' : 'Not Yet'}" data-level="${i}">${i}</div>`;
                    }

                    row += `
              </div>
            </td>
            <td>${item.CreatedAt}</td>
            <td>
              <div class="d-flex flex-wrap gap-1">
        `;

                    // Add appropriate action buttons
                    if (canApprove && item.Status !== 'Approved') {
                        row += `
            <button class="btn-cyber btn-cyber-green btn-sm action-btn approve"
                    data-id="${item.ScrapId}"
                    data-level="${currentLevel}"
                    onclick="handleApproval(${item.ScrapId}, true)">
                <i class="bi bi-check"></i> Approve
            </button>
            <button class="btn-cyber btn-cyber-red btn-sm action-btn reject"
                    data-id="${item.ScrapId}"
                    data-level="${currentLevel}"
                    onclick="handleApproval(${item.ScrapId}, false)">
                <i class="bi bi-x"></i> Reject
            </button>
          `;
                    }

                    if (canAddWeight && item.Status === 'Approved') {
                        row += `
            <button class="btn-cyber btn-sm action-btn add-weight"
                    data-scrap-id="${item.ScrapId}"
                    data-department="${item.Department}"
                    data-line="${item.Line}"
                    data-shift="${item.Shift}"
                    onclick="showWeightPrompt('${item.ScrapId}')">
                <i class="bi bi-plus-circle"></i> Add Weight
            </button>
          `;
                    }

                    row += `
              </div>
            </td>
          </tr>
        `;

                    scrapTable.innerHTML += row;
                });

                // Reinitialize checkboxes
                document.querySelectorAll('.row-checkbox').forEach(checkbox => {
                    checkbox.addEventListener('change', updateBulkButtons);
                });

                updateBulkButtons();
            } else {
                scrapTable.innerHTML = '<tr><td colspan="9" class="text-center">No scrap items found for this location.</td></tr>';
            }
        })
        .catch(error => {
            console.error('Error loading scrap items:', error);
            scrapTable.innerHTML = '<tr><td colspan="9" class="text-center text-danger">Error loading scrap items. Please try again.</td></tr>';
        });
}

/**
 * Set up bulk approval handling
 */
function setupBulkApprovalHandling() {
    console.log("Setting up bulk approval handling...");

    // Set up bulk action buttons
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    if (bulkApproveBtn) {
        bulkApproveBtn.addEventListener('click', function () {
            bulkApproveRejectItems(true);
        });
    }

    const bulkRejectBtn = document.getElementById('bulk-reject-btn');
    if (bulkRejectBtn) {
        bulkRejectBtn.addEventListener('click', function () {
            bulkApproveRejectItems(false);
        });
    }

    // Initialize select all checkbox
    const selectAllCheckbox = document.getElementById('selectAll');
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', toggleAllRows);
    }
}

/**
 * Handle individual item approval or rejection
 */
function handleApproval(scrapId, isApproved) {
    console.log(`Handling approval for scrap ID ${scrapId}, isApproved: ${isApproved}`);

    if (!isApproved) {
        // For rejections, show the rejection reason modal
        const rejectModal = document.getElementById('rejectReasonModal');
        if (rejectModal) {
            // Set the scrap ID in the form
            const scrapIdField = document.getElementById('rejectScrapId');
            if (scrapIdField) {
                scrapIdField.value = scrapId;
            }

            // Set bulk flag to false
            const bulkFlag = document.getElementById('isBulkReject');
            if (bulkFlag) {
                bulkFlag.value = 'false';
            }

            // Clear any previous reason
            const reasonInput = document.getElementById('rejectReasonInput');
            if (reasonInput) {
                reasonInput.value = '';
            }

            // Clear error message
            const errorElement = document.getElementById('rejectError');
            if (errorElement) {
                errorElement.style.display = 'none';
                errorElement.textContent = '';
            }

            // Show the modal
            rejectModal.style.display = 'flex';
        } else {
            // If modal not found, proceed without reason
            submitApproval(scrapId, false);
        }
    } else {
        // For approvals, submit directly
        submitApproval(scrapId, true);
    }
}

/**
 * Submit approval or rejection
 */
function submitApproval(scrapId, isApproved, rejectReason = null) {
    console.log(`Submitting ${isApproved ? 'approval' : 'rejection'} for scrap ID ${scrapId}`);

    // Get token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Show loading state
    const actionButtons = document.querySelectorAll(`.action-btn[data-id="${scrapId}"]`);
    const originalContents = [];

    actionButtons.forEach(button => {
        originalContents.push(button.innerHTML);
        button.innerHTML = '<i class="bi bi-arrow-repeat spin"></i>';
        button.disabled = true;
    });

    // Submit approval/rejection
    fetch('/ScrapApproval/HandleApproval', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapIds: [parseInt(scrapId)],
            IsApproved: isApproved,
            RejectReason: rejectReason
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            actionButtons.forEach((button, index) => {
                button.innerHTML = originalContents[index];
                button.disabled = false;
            });

            if (data.success) {
                // Show success notification
                const message = isApproved ?
                    'Item approved successfully' :
                    'Item rejected successfully';

                showNotification('Success', message, 'success');

                // Reload scrap items to reflect the update
                loadScrapItems();

                // Update location stats
                updateLocationStats();
            } else {
                // Show error
                let errorMessage = data.message || 'Error processing request';

                // Special handling for weight requirement error
                if (data.errorCode === 'weight_required') {
                    showNotification('Weight Required', errorMessage, 'warning');
                } else {
                    showNotification('Error', errorMessage, 'danger');
                }
            }
        })
        .catch(error => {
            console.error('Error:', error);

            // Reset button state
            actionButtons.forEach((button, index) => {
                button.innerHTML = originalContents[index];
                button.disabled = false;
            });

            // Show error
            showNotification('Error', 'An error occurred while processing the request', 'danger');
        });
}

/**
 * Submit rejection reason from modal
 */
function submitRejection() {
    console.log("Submitting rejection...");

    const scrapId = document.getElementById('rejectScrapId')?.value;
    const isBulkReject = document.getElementById('isBulkReject')?.value === 'true';
    const reason = document.getElementById('rejectReasonInput')?.value;

    if (!reason || reason.trim() === '') {
        showErrorMessage('rejectError', 'Please provide a reason for rejection');
        return;
    }

    if (isBulkReject) {
        // Get all selected scrap IDs
        const scrapIds = document.getElementById('rejectScrapIds')?.value.split(',').map(id => parseInt(id.trim()));

        if (!scrapIds || scrapIds.length === 0) {
            showErrorMessage('rejectError', 'No items selected for rejection');
            return;
        }

        // Submit bulk rejection
        submitBulkApproval(scrapIds, false, reason);
    } else {
        // Submit single rejection
        submitApproval(scrapId, false, reason);
    }

    // Close the modal
    hideModal('rejectReasonModal');
}

/**
 * Bulk approve or reject selected items
 */
function bulkApproveRejectItems(isApproved) {
    console.log(`Bulk ${isApproved ? 'approve' : 'reject'} initiated`);

    // Get selected items
    const selectedCheckboxes = document.querySelectorAll('.row-checkbox:checked');

    if (selectedCheckboxes.length === 0) {
        showNotification('No Items Selected', 'Please select at least one item', 'warning');
        return;
    }

    const scrapIds = Array.from(selectedCheckboxes).map(cb => parseInt(cb.getAttribute('data-id')));

    if (isApproved) {
        // For approvals, submit directly
        submitBulkApproval(scrapIds, true);
    } else {
        // For rejections, show reason modal
        const rejectModal = document.getElementById('rejectReasonModal');
        if (rejectModal) {
            // Set the bulk flag
            const bulkFlag = document.getElementById('isBulkReject');
            if (bulkFlag) {
                bulkFlag.value = 'true';
            }

            // Store scrap IDs in the form
            const scrapIdsField = document.getElementById('rejectScrapIds');
            if (scrapIdsField) {
                scrapIdsField.value = scrapIds.join(',');
            }

            // Clear any previous reason
            const reasonInput = document.getElementById('rejectReasonInput');
            if (reasonInput) {
                reasonInput.value = '';
            }

            // Clear error message
            const errorElement = document.getElementById('rejectError');
            if (errorElement) {
                errorElement.style.display = 'none';
                errorElement.textContent = '';
            }

            // Show the modal
            rejectModal.style.display = 'flex';
        } else {
            // If modal not found, proceed without reason
            submitBulkApproval(scrapIds, false);
        }
    }
}

/**
 * Submit bulk approval/rejection
 */
function submitBulkApproval(scrapIds, isApproved, rejectReason = null) {
    console.log(`Submitting bulk ${isApproved ? 'approval' : 'rejection'} for ${scrapIds.length} items`);

    // Get token
    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Show loading state
    const bulkBtn = isApproved ?
        document.getElementById('bulk-approve-btn') :
        document.getElementById('bulk-reject-btn');

    let originalContent = '';

    if (bulkBtn) {
        originalContent = bulkBtn.innerHTML;
        bulkBtn.innerHTML = '<i class="bi bi-arrow-repeat spin"></i>';
        bulkBtn.disabled = true;
    }

    // Submit bulk approval/rejection
    fetch('/ScrapApproval/HandleApproval', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapIds: scrapIds,
            IsApproved: isApproved,
            RejectReason: rejectReason
        })
    })
        .then(response => response.json())
        .then(data => {
            // Reset button state
            if (bulkBtn) {
                bulkBtn.innerHTML = originalContent;
                bulkBtn.disabled = false;
            }

            if (data.success) {
                // Show success notification
                const message = isApproved ?
                    `Successfully approved ${scrapIds.length} items` :
                    `Successfully rejected ${scrapIds.length} items`;

                showNotification('Success', message, 'success');

                // Reload scrap items to reflect the update
                loadScrapItems();

                // Update location stats
                updateLocationStats();

                // Uncheck "select all" checkbox
                const selectAllCheckbox = document.getElementById('selectAll');
                if (selectAllCheckbox) {
                    selectAllCheckbox.checked = false;
                }
            } else {
                // Show error
                let errorMessage = data.message || 'Error processing request';

                // Special handling for weight requirement error
                if (data.errorCode === 'weight_required') {
                    showNotification('Weight Required', errorMessage, 'warning');
                } else {
                    showNotification('Error', errorMessage, 'danger');
                }
            }
        })
        .catch(error => {
            console.error('Error:', error);

            // Reset button state
            if (bulkBtn) {
                bulkBtn.innerHTML = originalContent;
                bulkBtn.disabled = false;
            }

            // Show error
            showNotification('Error', 'An error occurred while processing the request', 'danger');
        });
}

/**
 * Toggle all rows in the scrap items table
 */
function toggleAllRows() {
    const selectAllCheckbox = document.getElementById('selectAll');
    const rowCheckboxes = document.querySelectorAll('.row-checkbox');

    if (selectAllCheckbox) {
        rowCheckboxes.forEach(cb => {
            cb.checked = selectAllCheckbox.checked;
        });

        updateBulkButtons();
    }
}

/**
 * Update bulk action buttons state based on selected items
 */
function updateBulkButtons() {
    const selectedCheckboxes = document.querySelectorAll('.row-checkbox:checked');
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    const bulkRejectBtn = document.getElementById('bulk-reject-btn');

    if (selectedCheckboxes.length > 0) {
        if (bulkApproveBtn) {
            bulkApproveBtn.disabled = false;
            bulkApproveBtn.classList.remove('disabled');
        }

        if (bulkRejectBtn) {
            bulkRejectBtn.disabled = false;
            bulkRejectBtn.classList.remove('disabled');
        }
    } else {
        if (bulkApproveBtn) {
            bulkApproveBtn.disabled = true;
            bulkApproveBtn.classList.add('disabled');
        }

        if (bulkRejectBtn) {
            bulkRejectBtn.disabled = true;
            bulkRejectBtn.classList.add('disabled');
        }
    }
}

/**
 * Listen for location changes and refresh data
 */
function listenForLocationChanges() {
    console.log("Setting up location change listener...");

    // Listen for explicit location updates
    const locationObserver = new MutationObserver(function (mutations) {
        mutations.forEach(function (mutation) {
            if (mutation.type === 'attributes' &&
                (mutation.attributeName === 'data-department' ||
                    mutation.attributeName === 'data-line' ||
                    mutation.attributeName === 'data-shift')) {
                console.log("Location changed, refreshing data");

                // Update stats
                updateLocationStats();

                // Reload scrap items
                loadScrapItems();

                // Reload weight history
                loadWeightHistory();
            }
        });
    });

    // Observe location value elements
    const departmentEl = document.querySelector('.location-value[data-department]');
    const lineEl = document.querySelector('.location-value[data-line]');
    const shiftEl = document.querySelector('.location-value[data-shift]');

    if (departmentEl) locationObserver.observe(departmentEl, { attributes: true });
    if (lineEl) locationObserver.observe(lineEl, { attributes: true });
    if (shiftEl) locationObserver.observe(shiftEl, { attributes: true });
}

/**
 * Show notification to the user
 */
function showNotification(title, message, type) {
    console.log(`Showing notification: ${title} - ${message} (${type})`);

    // Try to use toast if available
    const toast = document.getElementById('notificationToast');
    const toastBody = document.getElementById('toastBody');

    if (toast && toastBody && typeof bootstrap !== 'undefined') {
        // Set toast content
        toastBody.textContent = message;

        // Set toast color based on type
        toast.className = 'toast align-items-center text-white';

        if (type === 'success') {
            toast.classList.add('bg-success');
        } else if (type === 'danger') {
            toast.classList.add('bg-danger');
        } else if (type === 'warning') {
            toast.classList.add('bg-warning');
            toast.classList.remove('text-white');
            toast.classList.add('text-dark');
        } else {
            toast.classList.add('bg-info');
        }

        // Show toast
        const bsToast = new bootstrap.Toast(toast);
        bsToast.show();
    } else {
        // Fallback to custom notification
        // Create notification element
        const notification = document.createElement('div');
        notification.className = `alert-cyber ${type}`;
        notification.innerHTML = `
      <h3 class="mb-2">${title}</h3>
      <p>${message}</p>
      <button type="button" class="btn-cyber mt-2">
        <i class="bi bi-x-circle"></i> Close
      </button>
    `;

        // Find a good place to insert the notification
        const container = document.querySelector('.dashboard-container');
        const firstCard = container?.querySelector('.card');

        if (container) {
            if (firstCard) {
                container.insertBefore(notification, firstCard);
            } else {
                container.appendChild(notification);
            }

            // Add close button handler
            notification.querySelector('button').addEventListener('click', function () {
                notification.remove();
            });

            // Auto-remove after 5 seconds
            setTimeout(() => {
                if (document.body.contains(notification)) {
                    notification.remove();
                }
            }, 5000);
        } else {
            // Last resort - use alert
            alert(`${title}: ${message}`);
        }
    }
}

/**
 * Show error message in a specific element
 */
function showErrorMessage(selector, message) {
    console.log(`Showing error message in ${selector}: ${message}`);

    const errorElement = typeof selector === 'string' ? document.querySelector(selector) : selector;

    if (errorElement) {
        errorElement.textContent = message;
        errorElement.style.display = 'block';
    } else {
        // Fallback to notification
        showNotification('Error', message, 'danger');
    }
}

/**
 * Hide a modal by its ID
 */
function hideModal(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        modal.style.display = 'none';
    }
}

// Make helper functions globally available
window.showWeightPrompt = showWeightPrompt;
window.handleApproval = handleApproval;
window.toggleAllRows = toggleAllRows;
window.toggleAllLocations = toggleAllLocations;
window.updateSelectedLocations = updateSelectedLocations;
window.hideModal = hideModal;
window.updateBulkButtons = updateBulkButtons;