/**
 * Enhanced functionality for password copying and success notification
 * This script can be added directly to your dashboard page
 */

// Set up the copy password functionality
function setupCopyPasswordFeature() {
    console.log("Setting up copy password functionality");

    // Ensure the copy function is available globally
    window.copyPassword = function (elementId) {
        const passwordElement = document.getElementById(elementId);
        if (!passwordElement) {
            console.error(`Element with ID ${elementId} not found`);
            return;
        }

        // Select the text
        passwordElement.select();
        passwordElement.setSelectionRange(0, 99999); // For mobile devices

        // Try to copy with execCommand first (older browsers)
        let successful = false;
        try {
            successful = document.execCommand('copy');
            console.log(`Copy attempt with execCommand: ${successful ? 'successful' : 'failed'}`);
        } catch (err) {
            console.warn('execCommand copy failed:', err);
        }

        // If that fails, try the modern clipboard API
        if (!successful && navigator.clipboard) {
            navigator.clipboard.writeText(passwordElement.value)
                .then(() => {
                    console.log('Clipboard API copy successful');
                    showCopySuccess();
                })
                .catch(err => {
                    console.error('Clipboard API copy failed:', err);
                });
        } else if (successful) {
            showCopySuccess();
        }

        function showCopySuccess() {
            // Show success message
            const copySuccess = document.getElementById('copySuccess');
            if (copySuccess) {
                copySuccess.style.display = 'block';

                // Hide after 2 seconds
                setTimeout(() => {
                    copySuccess.style.display = 'none';
                }, 2000);
            } else {
                // If the element doesn't exist, create a temporary one
                const successMessage = document.createElement('div');
                successMessage.className = 'copy-success';
                successMessage.textContent = 'Password copied!';
                successMessage.style.display = 'block';
                successMessage.style.position = 'absolute';
                successMessage.style.left = '50%';
                successMessage.style.transform = 'translateX(-50%)';
                successMessage.style.bottom = '-25px';
                successMessage.style.color = 'var(--success-color)';

                // Add to the password field
                const passwordField = passwordElement.closest('.password-field');
                if (passwordField) {
                    passwordField.style.position = 'relative';
                    passwordField.appendChild(successMessage);

                    // Remove after 2 seconds
                    setTimeout(() => {
                        passwordField.removeChild(successMessage);
                    }, 2000);
                }
            }
        }
    };

    // Add click handlers to any copy buttons on the page
    document.querySelectorAll('.copy-btn').forEach(btn => {
        // Clone and replace to remove any existing event handlers
        const newBtn = btn.cloneNode(true);
        btn.parentNode.replaceChild(newBtn, btn);

        // Add new event handler
        newBtn.addEventListener('click', function () {
            const passwordField = this.closest('.password-field');
            if (passwordField) {
                const passwordInput = passwordField.querySelector('input[type="text"]');
                if (passwordInput) {
                    copyPassword(passwordInput.id);
                }
            }
        });
    });

    console.log("Copy password functionality setup complete");
}

// Handle success response with password display
function handleSuccessWithPassword(data) {
    console.log("Handling success with password");

    // Check if we have a temporary password
    if (data && data.tempPassword) {
        // Check if we already have a password display section
        let passwordDisplay = document.querySelector('.password-display');

        // If not, create one based on our template
        if (!passwordDisplay) {
            const template = document.querySelector('.password-display-template');
            if (template) {
                // Clone the template
                const clone = template.cloneNode(true);
                clone.classList.remove('password-display-template');
                clone.style.display = 'block';

                // Find a good place to insert it
                const successMessage = document.querySelector('.alert-cyber.success');
                if (successMessage) {
                    // Insert before the Close button
                    const closeBtn = successMessage.querySelector('.btn-cyber');
                    if (closeBtn) {
                        successMessage.insertBefore(clone, closeBtn);
                    } else {
                        successMessage.appendChild(clone);
                    }

                    // Update password display with data
                    populatePasswordDisplay(successMessage, data);
                } else {
                    // Create a success message with password display
                    createSuccessMessageWithPassword(data);
                }
            } else {
                console.error("Password display template not found");

                // Create a simple version instead
                createSimplePasswordDisplay(data);
            }
        } else {
            // Update the existing password display
            const container = passwordDisplay.closest('.alert-cyber.success');
            if (container) {
                populatePasswordDisplay(container, data);
            } else {
                populatePasswordDisplay(document.body, data);
            }
        }
    } else {
        console.log("No password data found in response");
    }
}

// Populate password display with data
function populatePasswordDisplay(container, data) {
    // Find the password input and update it
    const passwordInput = container.querySelector('#tempPassword');
    if (passwordInput) {
        passwordInput.value = data.tempPassword || '';
    }

    // Update login information
    const credentialType = container.querySelector('#userCredentialType');
    const credentialValue = container.querySelector('#userCredentialValue');
    const loginInstructions = container.querySelector('#loginInstructions');

    if (credentialType && credentialValue && loginInstructions) {
        if (data.username && data.username !== data.clockNumber) {
            credentialType.textContent = 'Username:';
            credentialValue.textContent = data.username;
            loginInstructions.textContent = 'Login with Username';
        } else {
            credentialType.textContent = 'Clock Number:';
            credentialValue.textContent = data.clockNumber || '';
            loginInstructions.textContent = 'Login with Clock Number';
        }
    }

    // Ensure copy button works
    setupCopyPasswordFeature();
}

// Create a success message with password display
function createSuccessMessageWithPassword(data) {
    // Get the template
    const template = document.querySelector('.success-message-template');
    if (!template) {
        console.error("Success message template not found");
        return;
    }

    // Clone the template
    const clone = template.cloneNode(true);
    clone.classList.remove('success-message-template');
    clone.style.display = 'block';

    // Update message
    const title = clone.querySelector('#successMessageTitle');
    const text = clone.querySelector('#successMessageText');

    if (title) title.textContent = "User Access Granted";
    if (text) text.textContent = "User access has been granted successfully. A temporary password has been generated.";

    // Find a good place to insert it - top of the dashboard container
    const container = document.querySelector('.dashboard-container');
    if (container) {
        container.insertBefore(clone, container.firstChild);

        // Now add the password display
        const passwordTemplate = document.querySelector('.password-display-template');
        if (passwordTemplate) {
            const passwordClone = passwordTemplate.cloneNode(true);
            passwordClone.classList.remove('password-display-template');
            passwordClone.style.display = 'block';

            // Insert before the Close button
            const closeBtn = clone.querySelector('.btn-cyber');
            if (closeBtn) {
                clone.insertBefore(passwordClone, closeBtn);
            } else {
                clone.appendChild(passwordClone);
            }

            // Populate with data
            populatePasswordDisplay(clone, data);
        }
    }
}

// Create a simpler version of the password display
function createSimplePasswordDisplay(data) {
    const display = document.createElement('div');
    display.className = 'alert-cyber success';

    display.innerHTML = `
        <h3 class="mb-2">User Access Granted</h3>
        <div class="password-display">
            <p class="mb-2">Temporary Password:</p>
            <div class="password-field">
                <input type="text" id="tempPassword" value="${data.tempPassword || ''}" 
                       class="password-value" readonly onclick="this.select()">
                <button type="button" class="copy-btn" onclick="copyPassword('tempPassword')">
                    <i class="bi bi-clipboard"></i>
                </button>
            </div>
            <div id="copySuccess" class="copy-success">Password copied!</div>
            
            <p class="mb-2 mt-3">Login Details:</p>
            <p class="mb-2">
                ${data.username && data.username !== data.clockNumber
            ? `Username: <strong>${data.username}</strong><br>Login with Username`
            : `Clock Number: <strong>${data.clockNumber || ''}</strong><br>Login with Clock Number`}
            </p>
        </div>
        <button type="button" class="btn-cyber mt-2" onclick="this.parentElement.style.display='none';">
            <i class="bi bi-x-circle"></i> Close
        </button>
    `;

    // Find a good place to insert it
    const container = document.querySelector('.dashboard-container');
    if (container) {
        container.insertBefore(display, container.firstChild);

        // Setup copy functionality
        setupCopyPasswordFeature();
    }
}

// Initialize everything when the document is ready
document.addEventListener('DOMContentLoaded', function () {
    console.log("Initializing enhanced password functionality");

    // Set up copy password feature
    setupCopyPasswordFeature();

    // Check if we have a success message with temp password
    const tempPasswordField = document.getElementById('tempPassword');
    if (tempPasswordField) {
        console.log("Found existing password field, ensuring functionality");

        // Make sure it's selectable
        tempPasswordField.onclick = function () { this.select(); };

        // Check for copy button
        const copyBtn = document.querySelector('.copy-btn');
        if (copyBtn) {
            console.log("Found copy button, ensuring click handler");
            copyBtn.onclick = function () { copyPassword('tempPassword'); };
        }
    }

    // Check for TempData from backend response
    if (typeof tempData !== 'undefined' && tempData.TempPassword) {
        console.log("Found TempData with password, handling success");
        handleSuccessWithPassword({
            tempPassword: tempData.TempPassword,
            username: tempData.Username,
            clockNumber: tempData.ClockNumber
        });
    }
});

// Make functions globally available
window.copyPassword = copyPassword || function () { };
window.setupCopyPasswordFeature = setupCopyPasswordFeature;
window.handleSuccessWithPassword = handleSuccessWithPassword;