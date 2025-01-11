const form = document.querySelector(".form-container");
const responseMessage = document.getElementById("responseMessage");
const token = localStorage.getItem("token");

document.addEventListener("DOMContentLoaded", () => {
  if (token) {
    localStorage.clear();
  }
});

form.addEventListener("submit", async (event) => {
  event.preventDefault();

  const username = document.getElementById("username").value;
  const password = document.getElementById("password").value;

  // Check if we are on register page
  const isRegisterPage = window.location.pathname.includes("/register");

  if (username.length < 2 || username.length > 20) {
    showResponseMessage(
      "Your username must be between 2 and 20 characters long",
      "error"
    );
    return;
  }

  if (password.length < 8 || password.length > 24) {
    showResponseMessage(
      "Your password must be between 8 and 24 characters long",
      "error"
    );
    return;
  }

  // Handle confirm password field for register page
  if (isRegisterPage) {
    const confirmPassword = document.getElementById("confirm_password").value;

    if (password !== confirmPassword) {
      showResponseMessage("Passwords do not match!", "error");
      return;
    }
  }

  try {
    // Find correct API endpoint (login or register)
    const endpoint = isRegisterPage ? "/api/user/register" : "/api/user/login";
    console.log(`${API_URL}${endpoint}`);
    const response = await fetch(`${API_URL}${endpoint}`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ username, password }),
    });

    if (response.ok) {
      const token = await response.text();
      showResponseMessage(
        `${
          isRegisterPage ? "Registration" : "Login"
        } successful! Redirecting...`,
        "success"
      );

      // Save token to localStorage
      localStorage.setItem("token", token);

      setTimeout(() => {
        window.location.href = `/user/${username}`;
      }, 1000);
    } else {
      const errorMessage = await response.text();
      showResponseMessage(
        `${isRegisterPage ? "Registration" : "Login"} failed: ${errorMessage}`,
        "error"
      );
    }
  } catch (error) {
    showResponseMessage(`Error: ${error.message}`, "error");
  }
});

// Toggle password visibility
function togglePasswordVisibility(passwordId, toggleId) {
  const password = document.getElementById(passwordId);
  const toggle = document.getElementById(toggleId);

  if (this.checked) {
    password.setAttribute("type", "text");
    toggle.textContent = "HIDE";
  } else {
    password.setAttribute("type", "password");
    toggle.textContent = "SHOW";
  }
}

document.getElementById("check1").onclick = function () {
  togglePasswordVisibility.call(this, "password", "toggle");
};

const check2 = document.getElementById("check2");
if (check2) {
  check2.onclick = function () {
    togglePasswordVisibility.call(this, "confirm_password", "toggle2");
  };
}

// Function to show the message and hide it after 3 seconds
function showResponseMessage(message, type) {
  responseMessage.textContent = message;
  responseMessage.className = `response show ${type}`;

  setTimeout(() => {
    responseMessage.classList.remove("show");
  }, 3000);
}
