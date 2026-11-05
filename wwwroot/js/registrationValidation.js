// Constants
const MANAGEMENT_ROLES = ['GroupLeader', 'Manager', 'SeniorManager', 'Admin'];
const COMPANY_DOMAIN = '@hesto.co.za';

// Email validation function
function isValidCompanyEmail(email) {
    console.log('Validating email:', email);

    if (!email) {
        console.log('Failed: Email is empty');
        return false;
    }

    email = email.toLowerCase();
    console.log('Lowercase email:', email);

    if (!email.endsWith(COMPANY_DOMAIN)) {
        console.log('Failed: Invalid domain');
        return false;
    }

    const username = email.slice(0, -(COMPANY_DOMAIN.length));
    console.log('Username part:', username);

    if (username.length < 1) {
        console.log('Failed: Username too short');
        return false;
    }

    const isValidFormat = /^[a-z0-9.]+$/.test(username);
    console.log('Username format valid?', isValidFormat);
    return isValidFormat;
}

// Department and Line handling
async function loadDepartments() {
    try {
        const response = await fetch('/Account/GetDepartments');
        const result = await response.json();

        if (result.success && result.departments) {
            const select = $('#departmentSelect');
            select.empty().append('<option value="">Select Department</option>');

            result.departments.forEach(dept => {
                select.append(`<option value="${dept.id}">${dept.name}</option>`);
            });
        }
    } catch (error) {
        console.error('Error loading departments:', error);
    }
}

$(document).ready(function () {
    $('#userType').on('change', handleUserTypeChange);
    loadDepartments();

    // Form submission
    $('#registrationForm').on('submit', function (e) {
        e.preventDefault();
        const formData = new FormData(this);
        const validation = validateRegistrationForm(formData);

        if (!validation.isValid) {
            showError(validation.message);
            return;
        }

        submitRegistration(formData);
    });
});

// Complete form validation
function validateRegistrationForm(formData) {
    const userType = formData.get('UserType');
    const isManagement = MANAGEMENT_ROLES.includes(userType);

    // Basic validation
    if (!userType || !formData.get('Name') || !formData.get('Surname') ||
        !formData.get('Shift') || !formData.get('DepartmentId')) {
        return {
            isValid: false,
            message: 'Please fill in all required fields'
        };
    }

    // Management role validation
    if (isManagement) {
        const email = formData.get('Email');
        if (!email || !email.toLowerCase().endsWith('@hesto.co.za')) {
            return {
                isValid: false,
                message: 'Please enter a valid company email (@hesto.co.za)'
            };
        }
    } else {
        // Non-management validation
        if (!formData.get('LineId')) {
            return {
                isValid: false,
                message: 'Please select a line'
            };
        }
    }

    return { isValid: true };
}
function validateForm(formData) {
    const userType = formData.get('UserType');
    const isManagement = MANAGEMENT_ROLES.includes(userType);

    if (!isManagement && !formData.get('ClockNumber')) {
        return { isValid: false, message: 'Clock Number is required for non-management roles' };
    }

    return { isValid: true };
}
// Helper function to test email validation
function testEmailValidation(email) {
    console.log('\nTesting email:', email);
    const isValid = isValidCompanyEmail(email);
    console.log('Final result:', isValid);
    return isValid;
}

// Helper function to format phone numbers
function formatPhoneNumber(phone) {
    const cleaned = phone.replace(/\D/g, '');
    if (cleaned.length === 10) {
        return cleaned.replace(/(\d{3})(\d{3})(\d{4})/, '$1 $2 $3');
    }
    return phone;
}

// Add to your registrationValidation.js
function handleUserTypeChange() {
    const userType = $('#userType').val();
    const isManagement = MANAGEMENT_ROLES.includes(userType);
    const lineGroup = $('#lineSelect').closest('.input-group');
    const emailInput = $('input[name="Email"]');
    const emailLabel = emailInput.closest('.input-group').find('label');

    // Handle line visibility
    if (isManagement) {
        lineGroup.hide();
        $('#lineSelect').prop('required', false);
        // Show and require company email for management
        emailLabel.text('COMPANY EMAIL (REQUIRED)');
        emailInput.prop('required', true)
            .attr('placeholder', 'name.surname@hesto.co.za');
    } else {
        lineGroup.show();
        $('#lineSelect').prop('required', true);
        // Make email optional for non-management
        emailLabel.text('EMAIL (OPTIONAL)');
        emailInput.prop('required', false)
            .attr('placeholder', '');
    }
}

// Export for use in other files if using modules
if (typeof module !== 'undefined' && module.exports) {
    module.exports = {
        MANAGEMENT_ROLES,
        COMPANY_DOMAIN,
        isValidCompanyEmail,
        validateRegistrationForm,
        testEmailValidation,
        formatPhoneNumber
    };
}