/**
 * Enhanced Modal Functionality for Scrap Management System
 * This script fixes issues with user management modals and ensures proper styling and functionality
 */

document.addEventListener('DOMContentLoaded', function () {
    console.log("Loading modal fixes...");

    // Fix modal display and z-index issues
    fixModalStyles();

    // Fix button event handlers
    attachModalButtonHandlers();

    // Enable copy password functionality
    setupCopyPasswordFeature();

    console.log("Modal fixes applied successfully");
});

/**
 * Fix modal display and z-index issues
 */
function fixModalStyles() {
    // Add styles to ensure modal displays correctly
    const styleEl = document.createElement('style');
    styleEl.textContent = `
        /* Modal z-index fixes */
        .modal-cyber {
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            background-color: rgba(0, 0, 0, 0.7);
            z-index: 9999 !important;
            align-items: center;
            justify-content: center;
        }
        
        .modal-cyber .modal-dialog {
            z-index: 10000 !important;
            background-color: var(--dark-bg);
            border: 1px solid var(--primary-color);
            border-radius: var(--border-radius);
            box-shadow: var(--border-glow);
            width: 90%;
            max-width: 500px;
            margin: 0 auto;
            pointer-events: auto !important;
            position: relative;
        }
        
        .modal-cyber .modal-header,
        .modal-cyber .modal-body,
        .modal-cyber .modal-footer {
            padding: 1.5rem;
            pointer-events: auto !important;
        }
        
        .modal-cyber .modal-header {
            border-bottom: 1px solid rgba(0, 255, 252, 0.2);
            display: flex;
            align-items: center;
            justify-content: space-between;
        }
        
        .modal-cyber .modal-footer {
            border-top: 1px solid rgba(0, 255, 252, 0.2);
            display: flex;
            justify-content: flex-end;
            gap: 1rem;
        }
        
        /* Form controls in modals */
        .modal-cyber .form-control-cyber {
            width: 100%;
            background-color: rgba(0, 15, 14, 0.95);
            border: 1px solid var(--primary-color-dark);
            color: var(--primary-color);
            padding: 0.75rem;
            border-radius: 4px;
            margin-bottom: 1rem;
        }
        
        .modal-cyber .form-control-cyber:focus {
            outline: none;
            border-color: var(--primary-color);
            box-shadow: 0 0 10px rgba(0, 255, 252, 0.3);
        }
        
        /* Password display improvements */
        .password-display {
            background-color: rgba(0, 32, 30, 0.95);
            border: 1px solid var(--primary-color);
            border-radius: 4px;
            padding: 1rem;
            margin: 1rem 0;
        }
        
        .password-field {
            display: flex;
            align-items: center;
            gap: 0.5rem;
            margin: 0.75rem 0;
        }
        
        .password-value {
            flex: 1;
            font-family: monospace;
            background-color: rgba(0, 15, 14, 0.95);
            border: 1px solid var(--primary-color);
            color: var(--primary-color);
            padding: 0.75rem;
            border-radius: 4px;
            font-size: 1.1rem;
            text-align: center;
            user-select: all;
            cursor: text;
        }
        
        .copy-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 42px;
            height: 42px;
            background-color: transparent;
            border: 1px solid var(--primary-color);
            color: var(--primary-color);
            border-radius: 4px;
            cursor: pointer;
            transition: all 0.3s ease;
        }
        
        .copy-btn:hover {
            background-color: rgba(0, 255, 252, 0.1);
            box-shadow: 0 0 10px var(--primary-color);
        }
        
        .copy-success {
            display: none;
            color: var(--success-color);
            font-size: 0.875rem;
            margin-top: 0.25rem;
            text-align: center;
        }
    `;
    document.head.appendChild(styleEl);

    // Apply patch for Bootstrap modals - convert them to our custom modals if needed
    convertBootstrapModals();
}

/**
 * Convert Bootstrap modals to our custom modals for consistency
 */
function convertBootstrapModals() {
    // Find all Bootstrap modals
    const bsModals = document.querySelectorAll('.modal:not(.modal-cyber)');

    bsModals.forEach(modal => {
        // If this is already a cyber-modal, skip it
        if (modal.classList.contains('modal-cyber')) return;

        // Convert to cyber-modal
        modal.classList.add('modal-cyber');

        // Fix internal structure classes if needed
        const dialog = modal.querySelector('.modal-dialog');
        if (dialog) {
            // Ensure cyber styling
            dialog.style.backgroundColor = 'var(--dark-bg)';
            dialog.style.border = '1px solid var(--primary-color)';
            dialog.style.borderRadius = 'var(--border-radius)';
            dialog.style.boxShadow = 'var(--border-glow)';
        }

        // Convert form controls to cyber-styled ones
        const inputs = modal.querySelectorAll('input.form-control');
        inputs.forEach(input => {
            input.classList.remove('form-control');
            input.classList.add('form-control-cyber');
        });

        // Convert buttons to cyber-styled ones
        const buttons = modal.querySelectorAll('button.btn:not(.btn-cyber)');
        buttons.forEach(button => {
            if (button.classList.contains('btn-secondary')) {
                button.classList.remove('btn-secondary');
                button.classList.add('btn-cyber', 'btn-cyber-red');
            }
            else if (button.classList.contains('btn-primary')) {
                button.classList.remove('btn-primary');
                button.classList.add('btn-cyber', 'btn-cyber-green');
            }
            else {
                button.classList.remove('btn');
                button.classList.add('btn-cyber');
            }
        });

        // Replace standard close button
        const closeBtn = modal.querySelector('.btn-close');
        if (closeBtn) {
            const newCloseBtn = document.createElement('button');
            newCloseBtn.type = 'button';
            newCloseBtn.className = 'btn-close-cyber';
            newCloseBtn.innerHTML = '<i class="bi bi-x-lg"></i>';
            newCloseBtn.setAttribute('data-bs-dismiss', 'modal');
            newCloseBtn.setAttribute('aria-label', 'Close');

            // Replace original close button
            if (closeBtn.parentNode) {
                closeBtn.parentNode.replaceChild(newCloseBtn, closeBtn);
            }
        }
    });
}

/**
 * Attach event handlers to modal buttons
 */
function attachModalButtonHandlers() {
    // Fix New User button
    const newUserButton = document.getElementById('newUserButton');
    if (newUserButton) {
        // Remove any existing event listeners and replace with new one
        const newBtn = newUserButton.cloneNode(true);
        if (newUserButton.parentNode) {
            newUserButton.parentNode.replaceChild(newBtn, newUserButton);
        }

        newBtn.addEventListener('click', function (e) {
            e.preventDefault();
            showModal('newUserModal');
        });
    }

    // Fix Reset Password button
    const resetPasswordButton = document.getElementById('resetPasswordButton');
    if (resetPasswordButton) {
        // Remove any existing event listeners and replace with new one
        const newBtn = resetPasswordButton.cloneNode(true);
        if (resetPasswordButton.parentNode) {
            resetPasswordButton.parentNode.replaceChild(newBtn, resetPasswordButton);
        }

        newBtn.addEventListener('click', function (e) {
            e.preventDefault();
            showModal('resetPasswordModal');
        });
    }

    // Fix all close buttons in modals
    document.querySelectorAll('.modal-cyber .btn-close-cyber, .modal-cyber .btn-cyber-red[data-bs-dismiss="modal"]').forEach(btn => {
        btn.addEventListener('click', function () {
            hideModal(this.closest('.modal-cyber').id);
        });
    });

    // Fix form submission for new user
    const newUserForm = document.getElementById('newUserForm');
    if (newUserForm) {
        newUserForm.addEventListener('submit', function (e) {
            // Show spinner on the submit button
            const submitBtn = newUserForm.querySelector('[type="submit"]');
            if (submitBtn) {
                const originalText = submitBtn.innerHTML;
                submitBtn.innerHTML = '<span class="spinner"><i class="bi bi-arrow-repeat"></i></span> Processing...';
                submitBtn.disabled = true;

                // Re-enable after 10 seconds in case of error
                setTimeout(() => {
                    submitBtn.innerHTML = originalText;
                    submitBtn.disabled = false;
                }, 10000);
            }
        });
    }

    // Fix form submission for reset password
    const resetPasswordForm = document.getElementById('resetPasswordForm');
    if (resetPasswordForm) {
        resetPasswordForm.addEventListener('submit', function (e) {
            // Show spinner on the submit button
            const submitBtn = resetPasswordForm.querySelector('[type="submit"]');
            if (submitBtn) {
                const originalText = submitBtn.innerHTML;
                submitBtn.innerHTML = '<span class="spinner"><i class="bi bi-arrow-repeat"></i></span> Processing...';
                submitBtn.disabled = true;

                // Re-enable after 10 seconds in case of error
                setTimeout(() => {
                    submitBtn.innerHTML = originalText;
                    submitBtn.disabled = false;
                }, 10000);
            }
        });
    }
}

/**
 * Set up copy password feature
 */
function setupCopyPasswordFeature() {
    // Function to copy password to clipboard
    window.copyPassword = function (elementId) {
        const passwordElement = document.getElementById(elementId);
        if (passwordElement) {
            // Select the text
            passwordElement.select();
            passwordElement.setSelectionRange(0, 99999); // For mobile devices

            // Copy to clipboard
            try {
                const successful = document.execCommand('copy');

                if (successful) {
                    // Show success message
                    const copySuccess = document.getElementById('copySuccess');
                    if (copySuccess) {
                        copySuccess.style.display = 'block';
                        setTimeout(() => {
                            copySuccess.style.display = 'none';
                        }, 2000);
                    } else {
                        // Create success message if it doesn't exist
                        const successEl = document.createElement('div');
                        successEl.id = 'copySuccess';
                        successEl.className = 'copy-success';
                        successEl.style.display = 'block';
                        successEl.textContent = 'Password copied!';

                        const passwordField = passwordElement.closest('.password-field');
                        if (passwordField) {
                            passwordField.after(successEl);
                            setTimeout(() => {
                                successEl.style.display = 'none';
                            }, 2000);
                        }
                    }
                }
            } catch (err) {
                console.error('Failed to copy password: ', err);

                // Try modern clipboard API as fallback
                if (navigator.clipboard) {
                    navigator.clipboard.writeText(passwordElement.value)
                        .then(() => {
                            // Show success message
                            const copySuccess = document.getElementById('copySuccess');
                            if (copySuccess) {
                                copySuccess.style.display = 'block';
                                setTimeout(() => {
                                    copySuccess.style.display = 'none';
                                }, 2000);
                            }
                        })
                        .catch(err => {
                            console.error('Failed to copy password with Clipboard API: ', err);
                        });
                }
            }
        }
    };

    // Add click handlers to copy buttons
    document.querySelectorAll('.copy-btn').forEach(btn => {
        btn.addEventListener('click', function () {
            const passwordField = this.closest('.password-field');
            if (passwordField) {
                const passwordInput = passwordField.querySelector('input[type="text"]');
                if (passwordInput) {
                    copyPassword(passwordInput.id);
                }
            }
        });
    });
}

/**
 * Show a modal by ID
 */
function showModal(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        // Check if this is a Bootstrap modal or our custom modal
        if (modal.classList.contains('modal-cyber')) {
            modal.style.display = 'flex';
        } else if (typeof bootstrap !== 'undefined' && bootstrap.Modal) {
            const bsModal = new bootstrap.Modal(modal);
            bsModal.show();
        } else {
            // Fallback - convert to our custom modal and show it
            modal.classList.add('modal-cyber');
            modal.style.display = 'flex';
        }

        // Focus the first input
        setTimeout(() => {
            const firstInput = modal.querySelector('input:not([type="hidden"])');
            if (firstInput) {
                firstInput.focus();
            }
        }, 100);
    }
}

/**
 * Hide a modal by ID
 */
function hideModal(modalId) {
    const modal = document.getElementById(modalId);
    if (modal) {
        // Check if this is a Bootstrap modal or our custom modal
        if (modal.classList.contains('modal-cyber')) {
            modal.style.display = 'none';
        } else if (typeof bootstrap !== 'undefined' && bootstrap.Modal) {
            const bsModal = bootstrap.Modal.getInstance(modal);
            if (bsModal) {
                bsModal.hide();
            }
        } else {
            // Fallback
            modal.style.display = 'none';
        }
    }
}

// Make these functions globally available
window.showModal = showModal;
window.hideModal = hideModal;
window.copyPassword = copyPassword;