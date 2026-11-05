/**
 * TeamLeader Button Fix - FIXED VERSION (IMPROVED)
 * Ensures approve/reject buttons are disabled until weight is recorded
 * With fixes for BULK approvals as well
 */

document.addEventListener('DOMContentLoaded', function () {
    console.log("⭐ TeamLeader button fix loading (IMPROVED VERSION WITH BULK FIX) ⭐");

    // Check if user is TeamLeader
    const userType = document.querySelector('meta[name="user-type"]')?.content?.toLowerCase();
    const isTeamLeader = userType === 'teamleader';

    // Fix click handlers for all buttons (regardless of user type)
    fixBulkApprovalButtons();

    if (isTeamLeader) {
        console.log("TeamLeader detected - applying weight requirement");

        // Immediately disable approve/reject buttons
        disableApprovalButtons();

        // Set up the Save Weight button
        setupWeightSaving();

        // Check if weight has already been recorded
        checkWeightStatus();
    }
});

/**
 * Show a notification to the user
 * Using the global notification system to avoid conflicts
 */
function showTeamLeaderNotification(message, type = 'success') {
    // Try to use existing toast notification system
    const toast = document.getElementById('notificationToast');
    const toastBody = document.getElementById('toastBody');

    if (toast && toastBody) {
        toastBody.textContent = message;
        toast.className = toast.className.replace(/bg-\w+/, '');
        toast.classList.add(type === 'error' ? 'bg-danger' : 'bg-success');

        if (typeof bootstrap !== 'undefined' && bootstrap.Toast) {
            const bsToast = new bootstrap.Toast(toast);
            bsToast.show();
        } else {
            toast.style.display = 'block';
            setTimeout(() => { toast.style.display = 'none'; }, 5000);
        }
    } else {
        // Fallback to alert
        alert(message);
    }
}

/**
 * Disable all approval and rejection buttons
 */
function disableApprovalButtons() {
    console.log("Disabling all approval buttons until weight is recorded");

    // Disable individual approve buttons
    document.querySelectorAll('.action-btn.approve, button.approve, button[onclick*="approve"]').forEach(btn => {
        btn.disabled = true;
        btn.classList.add('disabled');

        // Store the scrapId for later
        const scrapIdMatch = (btn.getAttribute('onclick') || '').match(/handleApproval\((\d+)/);
        if (scrapIdMatch && scrapIdMatch[1]) {
            btn.setAttribute('data-scrap-id', scrapIdMatch[1]);
        }

        // Note: We're going to use event listeners instead of onclick attributes
        btn.setAttribute('data-onclick-disabled', 'true');
    });

    // Disable individual reject buttons
    document.querySelectorAll('.action-btn.reject, button.reject, button[onclick*="reject"]').forEach(btn => {
        btn.disabled = true;
        btn.classList.add('disabled');

        // Store the scrapId for later
        const scrapIdMatch = (btn.getAttribute('onclick') || '').match(/handleApproval\((\d+)/);
        if (scrapIdMatch && scrapIdMatch[1]) {
            btn.setAttribute('data-scrap-id', scrapIdMatch[1]);
        }

        btn.setAttribute('data-onclick-disabled', 'true');
    });

    // Disable bulk buttons
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    const bulkRejectBtn = document.getElementById('bulk-reject-btn');

    if (bulkApproveBtn) {
        bulkApproveBtn.disabled = true;
        bulkApproveBtn.classList.add('disabled');
    }

    if (bulkRejectBtn) {
        bulkRejectBtn.disabled = true;
        bulkRejectBtn.classList.add('disabled');
    }
}

/**
 * Enable all approval and rejection buttons
 */
function enableApprovalButtons() {
    console.log("Weight recorded - enabling approval buttons");

    // Enable individual approve buttons and add click listeners
    document.querySelectorAll('.action-btn.approve, button.approve, button[onclick*="approve"], button[data-onclick-disabled="true"]').forEach(btn => {
        btn.disabled = false;
        btn.classList.remove('disabled');

        // Get scrapId from data attribute or fall back to data-id
        const scrapId = btn.getAttribute('data-scrap-id') || btn.getAttribute('data-id');
        if (scrapId) {
            // Remove existing click event to avoid duplicates
            btn.removeEventListener('click', btn._approveHandler);

            // Add click handler that directly calls our function
            btn._approveHandler = function (e) {
                e.preventDefault();
                console.log("Approval button clicked for ScrapId: " + scrapId);
                handleApproval(parseInt(scrapId), true);
            };
            btn.addEventListener('click', btn._approveHandler);

            console.log(`Added approval click handler for button with ScrapId: ${scrapId}`);
        } else {
            console.warn("Could not find scrapId for approval button");
        }
    });

    // Enable individual reject buttons and add click listeners
    document.querySelectorAll('.action-btn.reject, button.reject, button[onclick*="reject"]').forEach(btn => {
        btn.disabled = false;
        btn.classList.remove('disabled');

        // Get scrapId from data attribute or fall back to data-id
        const scrapId = btn.getAttribute('data-scrap-id') || btn.getAttribute('data-id');
        if (scrapId) {
            // Remove existing click event to avoid duplicates
            btn.removeEventListener('click', btn._rejectHandler);

            // Add click handler that directly calls our function
            btn._rejectHandler = function (e) {
                e.preventDefault();
                console.log("Reject button clicked for ScrapId: " + scrapId);
                handleApproval(parseInt(scrapId), false);
            };
            btn.addEventListener('click', btn._rejectHandler);

            console.log(`Added reject click handler for button with ScrapId: ${scrapId}`);
        } else {
            console.warn("Could not find scrapId for reject button");
        }
    });

    // Enable bulk buttons if items are selected
    updateBulkButtons();
}

/**
 * Fix bulk approval/rejection button handlers
 * This is crucial since these weren't working
 */
function fixBulkApprovalButtons() {
    console.log("Setting up bulk approval/rejection buttons");

    // Fix bulk approve button
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    if (bulkApproveBtn) {
        // Clone and replace to remove any existing handlers
        const newBulkApproveBtn = bulkApproveBtn.cloneNode(true);
        bulkApproveBtn.parentNode.replaceChild(newBulkApproveBtn, bulkApproveBtn);

        // Add fresh event listener
        newBulkApproveBtn.addEventListener('click', function (e) {
            e.preventDefault();
            console.log("Bulk approve button clicked");
            handleBulkAction(true);
        });
        console.log("Bulk approve button handler attached");
    } else {
        console.warn("Bulk approve button not found");
    }

    // Fix bulk reject button
    const bulkRejectBtn = document.getElementById('bulk-reject-btn');
    if (bulkRejectBtn) {
        // Clone and replace to remove any existing handlers
        const newBulkRejectBtn = bulkRejectBtn.cloneNode(true);
        bulkRejectBtn.parentNode.replaceChild(newBulkRejectBtn, bulkRejectBtn);

        // Add fresh event listener
        newBulkRejectBtn.addEventListener('click', function (e) {
            e.preventDefault();
            console.log("Bulk reject button clicked");
            handleBulkAction(false);
        });
        console.log("Bulk reject button handler attached");
    } else {
        console.warn("Bulk reject button not found");
    }

    // Also fix the "select all" checkbox for good measure
    const selectAllCheckbox = document.getElementById('selectAll');
    if (selectAllCheckbox) {
        selectAllCheckbox.addEventListener('change', toggleAllRows);
        console.log("Select all checkbox handler attached");
    }

    // Add change listeners to all row checkboxes to update button state
    document.querySelectorAll('.row-checkbox').forEach(checkbox => {
        checkbox.addEventListener('change', function () {
            updateBulkButtons();
        });
    });

    // Initial update of bulk buttons
    updateBulkButtons();
}

/**
 * Update bulk buttons state based on checkbox selection
 */
function updateBulkButtons() {
    const bulkApproveBtn = document.getElementById('bulk-approve-btn');
    const bulkRejectBtn = document.getElementById('bulk-reject-btn');
    const selectedCount = document.querySelectorAll('.row-checkbox:checked').length;

    console.log(`Selected rows count: ${selectedCount}`);

    if (bulkApproveBtn) {
        if (selectedCount > 0) {
            bulkApproveBtn.disabled = false;
            bulkApproveBtn.classList.remove('disabled');
        } else {
            bulkApproveBtn.disabled = true;
            bulkApproveBtn.classList.add('disabled');
        }
    }

    if (bulkRejectBtn) {
        if (selectedCount > 0) {
            bulkRejectBtn.disabled = false;
            bulkRejectBtn.classList.remove('disabled');
        } else {
            bulkRejectBtn.disabled = true;
            bulkRejectBtn.classList.add('disabled');
        }
    }
}

/**
 * Set up the Save Weight button
 */
function setupWeightSaving() {
    // Find button with multiple possible selectors
    const saveBtn = document.getElementById('saveLocationWeightBtn') ||
        document.querySelector('.btn-success') ||
        document.querySelector('button:contains("Save Weight Record")');

    console.log("Found weight save button:", saveBtn);

    if (saveBtn) {
        // Clone and replace to remove any existing handlers
        const newSaveBtn = saveBtn.cloneNode(true);
        saveBtn.parentNode.replaceChild(newSaveBtn, saveBtn);

        // Add fresh event listener
        newSaveBtn.addEventListener('click', function (e) {
            e.preventDefault();
            console.log("Save weight button clicked");
            saveTeamLeaderWeight();
        });
    } else {
        console.error("Save weight button not found on page");
    }
}

/**
 * Save weight for TeamLeader's current location
 */
function saveTeamLeaderWeight() {
    console.log("Starting weight save process");
    // Get weight input field - try different methods to find it
    const weightInput = document.getElementById('locationWeightValue') ||
        document.querySelector('input[type="number"]') ||
        document.querySelector('input[placeholder*="weight"]');

    if (!weightInput) {
        showTeamLeaderNotification('Weight input field not found', 'error');
        return;
    }

    const weight = weightInput.value ? parseFloat(weightInput.value) : 0;

    if (!weight || weight <= 0) {
        showTeamLeaderNotification('Please enter a valid weight greater than zero', 'error');
        return;
    }

    // Get description input
    const descriptionInput = document.getElementById('locationWeightDescription') ||
        document.querySelector('input[placeholder*="description"]');

    const description = descriptionInput ? descriptionInput.value : '';

    // Get current location
    const dept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const line = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const shift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    if (!dept || !line || !shift) {
        showTeamLeaderNotification('Unable to determine current location', 'error');
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showTeamLeaderNotification('Security token not found', 'error');
        return;
    }

    // Show loading state
    const weightBtn = document.querySelector('.btn-success') || document.getElementById('saveLocationWeightBtn');
    const originalText = weightBtn?.innerHTML || 'Save Weight Record';

    if (weightBtn) {
        weightBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Saving...';
        weightBtn.disabled = true;
    }

    // Save the weight
    console.log("Submitting weight save request to server");
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
        .then(response => {
            console.log("Weight save response received, status:", response.status);
            return response.json();
        })
        .then(data => {
            // Reset button
            if (weightBtn) {
                weightBtn.innerHTML = originalText;
                weightBtn.disabled = false;
            }

            if (data.success) {
                console.log("Weight saved successfully!");
                showTeamLeaderNotification('Weight saved successfully', 'success');

                // Clear input fields
                if (weightInput) weightInput.value = '';
                if (descriptionInput) descriptionInput.value = '';

                // Important: After saving weight, enable approval
                console.log("Removing weight warnings and enabling approval buttons");
                removeWeightWarnings();
                enableApprovalButtons();
            } else {
                console.error("Weight save error:", data.message);
                showTeamLeaderNotification(data.message || 'Error saving weight', 'error');
            }
        })
        .catch(error => {
            console.error('Error saving weight:', error);
            if (weightBtn) {
                weightBtn.innerHTML = originalText;
                weightBtn.disabled = false;
            }
            showTeamLeaderNotification('Error saving weight. Please try again.', 'error');
        });
}

/**
 * Check if weight has already been recorded for the current location
 */
function checkWeightStatus() {
    // Get location details
    const dept = document.querySelector('.location-value[data-department]')?.textContent.trim();
    const line = document.querySelector('.location-value[data-line]')?.textContent.trim();
    const shift = document.querySelector('.location-value[data-shift]')?.textContent.trim();

    if (!dept || !line || !shift) {
        console.warn("Could not determine location for weight check");
        return;
    }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        return;
    }

    // Check if weight has been recorded for this location
    console.log(`Checking if location ${dept}/${line}/${shift} has recorded weight`);
    fetch(`/ScrapApproval/CheckLocationWeighed?department=${encodeURIComponent(dept)}&line=${encodeURIComponent(line)}&shift=${encodeURIComponent(shift)}`, {
        headers: {
            'RequestVerificationToken': token
        }
    })
        .then(response => response.json())
        .then(data => {
            if (data.success && data.hasBeenWeighed) {
                console.log("Weight already recorded for this location - enabling approval");
                removeWeightWarnings();
                enableApprovalButtons();
            } else {
                console.log("No weight recorded for this location - keeping approval disabled");
                ensureWeightWarningExists();
                disableApprovalButtons();
            }
        })
        .catch(error => {
            console.error("Error checking weight status:", error);
        });
}

/**
 * Remove weight warnings
 */
function removeWeightWarnings() {
    // Find and remove weight warning messages
    document.querySelectorAll('.alert').forEach(el => {
        if (el.textContent.includes('record weight')) {
            el.style.display = 'none';
        }
    });
}

/**
 * Ensure a weight warning exists
 */
function ensureWeightWarningExists() {
    // Check if a warning already exists and is visible
    let warningExists = false;

    document.querySelectorAll('.alert').forEach(el => {
        if (el.textContent.includes('record weight') &&
            window.getComputedStyle(el).display !== 'none') {
            warningExists = true;
        }
    });

    if (warningExists) {
        return;
    }

    // Add warning message
    const weightSection = document.getElementById('teamLeaderWeightSection');

    if (weightSection) {
        const reminderDiv = document.createElement('div');
        reminderDiv.className = 'alert alert-info mb-3';
        reminderDiv.innerHTML = '<i class="bi bi-info-circle"></i> Please record weight for this location before approving items.';

        const cardBody = weightSection.querySelector('.card-body');
        if (cardBody) {
            cardBody.insertBefore(reminderDiv, cardBody.firstChild);
        }
    }
}

/**
 * Handle approval or rejection of an item
 */
function handleApproval(scrapId, isApproved) {
    console.log(`Handling ${isApproved ? 'approval' : 'rejection'} for scrap ID: ${scrapId}`);

    if (!isApproved) {
        // Show rejection reason modal
        const rejectModal = document.getElementById('rejectReasonModal');
        if (rejectModal) {
            rejectModal.style.display = 'flex';
            document.getElementById('rejectScrapId').value = scrapId;
            document.getElementById('isBulkReject').value = 'false';

            // Set up confirm button handler
            const confirmRejectBtn = document.getElementById('confirmRejectBtn');
            if (confirmRejectBtn) {
                confirmRejectBtn.onclick = function () {
                    const reason = document.getElementById('rejectReasonInput').value;
                    if (!reason) {
                        document.getElementById('rejectError').textContent = 'Please provide a reason for rejection';
                        document.getElementById('rejectError').style.display = 'block';
                        return;
                    }

                    rejectModal.style.display = 'none';
                    processApproval(scrapId, false, reason);
                };
            }
        } else {
            // Direct handling without modal
            processApproval(scrapId, false);
        }
    } else {
        // Submit approval directly
        processApproval(scrapId, true);
    }
}

/**
 * Process the actual approval or rejection
 */
function processApproval(scrapId, isApproved, rejectReason = null) {
    console.log(`Processing ${isApproved ? 'approval' : 'rejection'} for ScrapId: ${scrapId}`);
    console.log(`Reject reason: ${rejectReason || 'None'}`);

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;

    if (!token) {
        console.error("CSRF token not found");
        showTeamLeaderNotification('Security token not found', 'error');
        return;
    }

    console.log("Submitting approval request to server");
    fetch('/ScrapApproval/HandleApproval', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapIds: [scrapId],
            IsApproved: isApproved,
            RejectReason: rejectReason
        })
    })
        .then(response => {
            console.log("Server responded with status:", response.status);
            return response.json();
        })
        .then(data => {
            console.log("Server response:", data);

            if (data.success) {
                showTeamLeaderNotification(`Item ${isApproved ? 'approved' : 'rejected'} successfully`, 'success');

                // Update UI to reflect the change
                const row = document.querySelector(`.row-checkbox[data-id="${scrapId}"]`)?.closest('tr');
                if (row) {
                    row.style.opacity = 0;
                    setTimeout(() => row.style.display = 'none', 500);
                } else {
                    // Try alternate way to find the row
                    const actionBtn = document.querySelector(`[data-scrap-id="${scrapId}"]`);
                    if (actionBtn) {
                        const row = actionBtn.closest('tr');
                        if (row) {
                            row.style.opacity = 0;
                            setTimeout(() => row.style.display = 'none', 500);
                        }
                    }
                }

                // Refresh the page after a short delay to update counts
                setTimeout(() => {
                    window.location.reload();
                }, 1000);
            } else {
                showTeamLeaderNotification(data.message || 'Error processing request', 'error');
            }
        })
        .catch(error => {
            console.error('Error in processApproval:', error);
            showTeamLeaderNotification('Error processing request', 'error');
        });
}

/**
 * Toggle selection of all rows
 */
function toggleAllRows() {
    const selectAllCheckbox = document.getElementById('selectAll');
    const allChecked = selectAllCheckbox ? selectAllCheckbox.checked : false;

    document.querySelectorAll('.row-checkbox').forEach(checkbox => {
        checkbox.checked = allChecked;
    });

    updateBulkButtons();
}

/**
 * Process bulk approval or rejection
 */
function handleBulkAction(isApproved) {
    console.log(`Starting bulk ${isApproved ? 'approval' : 'rejection'} process`);

    // Get all selected items
    const selectedCheckboxes = document.querySelectorAll('.row-checkbox:checked');
    const scrapIds = Array.from(selectedCheckboxes).map(cb => parseInt(cb.getAttribute('data-id')));

    if (!scrapIds.length) {
        showTeamLeaderNotification('No items selected for bulk action', 'error');
        console.warn('No scrap IDs found for bulk action');
        return;
    }

    console.log(`Found ${scrapIds.length} items for bulk ${isApproved ? 'approval' : 'rejection'}:`, scrapIds);

    if (!isApproved) {
        // For rejection, we need a reason - show modal
        const rejectModal = document.getElementById('rejectReasonModal');
        if (rejectModal) {
            rejectModal.style.display = 'flex';
            document.getElementById('rejectScrapIds').value = JSON.stringify(scrapIds);
            document.getElementById('isBulkReject').value = 'true';

            // Set up confirm button handler
            const confirmRejectBtn = document.getElementById('confirmRejectBtn');
            if (confirmRejectBtn) {
                // Override existing click handlers
                const newConfirmBtn = confirmRejectBtn.cloneNode(true);
                confirmRejectBtn.parentNode.replaceChild(newConfirmBtn, confirmRejectBtn);

                newConfirmBtn.addEventListener('click', function () {
                    const reason = document.getElementById('rejectReasonInput').value;
                    if (!reason) {
                        document.getElementById('rejectError').textContent = 'Please provide a reason for rejection';
                        document.getElementById('rejectError').style.display = 'block';
                        return;
                    }

                    rejectModal.style.display = 'none';
                    processBulkAction(scrapIds, false, reason);
                });
            }
        } else {
            console.warn('Reject reason modal not found');
            showTeamLeaderNotification('Rejection requires a reason, but the reason modal was not found', 'error');
        }
    } else {
        // For approval, process immediately
        processBulkAction(scrapIds, true);
    }
}

/**
 * Send bulk approval/rejection to server
 */
function processBulkAction(scrapIds, isApproval, rejectReason = null) {
    console.log(`Processing bulk ${isApproval ? 'approval' : 'rejection'} for ${scrapIds.length} items`);

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    if (!token) {
        showTeamLeaderNotification('Security token not found', 'error');
        return;
    }

    // Show loading on bulk button
    const bulkBtn = isApproval ?
        document.getElementById('bulk-approve-btn') :
        document.getElementById('bulk-reject-btn');

    let originalContent = '';
    if (bulkBtn) {
        originalContent = bulkBtn.innerHTML;
        bulkBtn.innerHTML = '<i class="bi bi-spinner bi-spin"></i> Processing...';
        bulkBtn.disabled = true;
    }

    // Make API call
    fetch('/ScrapApproval/HandleApproval', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': token
        },
        body: JSON.stringify({
            ScrapIds: scrapIds,
            IsApproved: isApproval,
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
                showTeamLeaderNotification(`Successfully ${isApproval ? 'approved' : 'rejected'} ${scrapIds.length} items`, 'success');

                // Remove rows from UI with fade out effect
                scrapIds.forEach(id => {
                    const row = document.querySelector(`.row-checkbox[data-id="${id}"]`)?.closest('tr');
                    if (row) {
                        row.style.transition = 'opacity 0.5s';
                        row.style.opacity = '0';
                        setTimeout(() => {
                            row.style.display = 'none';
                        }, 500);
                    }
                });

                // Uncheck select all
                const selectAllCheckbox = document.getElementById('selectAll');
                if (selectAllCheckbox) {
                    selectAllCheckbox.checked = false;
                    selectAllCheckbox.indeterminate = false;
                }

                // Update stats after a delay
                setTimeout(() => {
                    if (typeof window.updateScrapItemStats === 'function') {
                        window.updateScrapItemStats();
                    } else {
                        // Refresh page as fallback
                        window.location.reload();
                    }
                }, 1000);
            } else {
                if (data.errorCode === 'weight_required') {
                    showTeamLeaderNotification('You must record weight before approval', 'warning');

                    // Focus weight input field
                    const weightInput = document.getElementById('locationWeightValue');
                    if (weightInput) {
                        weightInput.focus();
                        weightInput.scrollIntoView({ behavior: 'smooth' });
                    }
                } else {
                    showTeamLeaderNotification(data.message || 'Error processing request', 'error');
                }
            }
        })
        .catch(error => {
            console.error('Error in bulk action:', error);

            // Reset button state
            if (bulkBtn) {
                bulkBtn.innerHTML = originalContent;
                bulkBtn.disabled = false;
            }

            showTeamLeaderNotification('Error processing request. Please try again.', 'error');
        });
}

// Make our functions globally available
window.disableApprovalButtons = disableApprovalButtons;
window.enableApprovalButtons = enableApprovalButtons;
window.updateBulkButtons = updateBulkButtons;
window.saveTeamLeaderWeight = saveTeamLeaderWeight;
window.handleApproval = handleApproval;
window.toggleAllRows = toggleAllRows;
window.processApproval = processApproval;
window.teamLeaderNotification = showTeamLeaderNotification;
window.handleBulkAction = handleBulkAction;
window.processBulkAction = processBulkAction;
window.fixBulkApprovalButtons = fixBulkApprovalButtons;