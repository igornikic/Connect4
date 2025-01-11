import showResponseMessage from "./resMessage.js";

const avatarContainer = document.getElementById("avatar_container");
avatarContainer.classList.add("loading");

const user = JSON.parse(localStorage.getItem("user"));
const token = localStorage.getItem("token");

// If we can't get user from localstorage redirect to login page
if (!user) {
  showResponseMessage("Profile not found. Redirecting to login...", "error");
  setTimeout(() => {
    window.location.href = "/";
  }, 3000);
}

// Calculate losses and winrate
let loses = user.TotalGamesPlayed - (user.TotalWins + user.TotalDraws);
if (isNaN(loses)) loses = 0;
const winRate =
  user.TotalGamesPlayed > 0
    ? ((user.TotalWins / user.TotalGamesPlayed) * 100).toFixed(2)
    : 0;

document.getElementById("menu").href = `/user/${user.Username}`;
document.getElementById("username").textContent = user.Username;

const img = document.getElementById("avatar_img");
img.src = `${API_URL}/api/user/${user.AvatarPath}`;
img.classList.add("avatar");
img.alt = "Avatar";

// Event listener to trigger displaying purchased avatars
img.addEventListener("click", () => {
  purchasedAvatars();
});

img.onload = function () {
  avatarContainer.classList.remove("loading"); // Remove loading spinner
  avatarContainer.appendChild(img); // Add image to container
};
img.onerror = function () {
  avatarContainer.classList.remove("loading");
  avatarContainer.textContent = img.alt; // Show alt text if image fails to load
};

document.getElementById("skill-score").textContent = user.SkillScore;
document.getElementById("total-games").textContent = user.TotalGamesPlayed;
document.getElementById("total-wins").textContent = user.TotalWins;
document.getElementById("total-draws").textContent = user.TotalDraws;
document.getElementById("loses").textContent = loses;
document.getElementById("winrate").textContent = `${winRate}%`;

async function purchasedAvatars() {
  // Clear any existing avatar options
  const avatarOptionsContainer = document.createElement("div");
  avatarOptionsContainer.id = "avatar_options";

  // Fetch purchased avatars from backend
  const response = await fetch(
    `${API_URL}/api/user/${user.Username}/purchased-avatars`,
    {
      method: "GET",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    }
  );

  if (response.ok) {
    const purchasedAvatars = await response.json();

    // Display purchased avatars as options
    purchasedAvatars.forEach((avatarId) => {
      const avatarImg = document.createElement("img");
      avatarImg.src = `${API_URL}/api/user/Assets/Avatars/avatar${avatarId}.png`;
      avatarImg.classList.add("avatar");
      avatarImg.alt = `Avatar ${avatarId}`;

      // Update the user's avatar on click
      avatarImg.addEventListener("click", () => {
        changeAvatar(avatarId);
      });

      avatarOptionsContainer.appendChild(avatarImg);
    });

    // Show the options
    const body = document.querySelector("body");
    body.appendChild(avatarOptionsContainer); // Show avatar options
  } else {
    showResponseMessage("Error fetching purchased avatars", "error");
  }
}

// Function to change avatar
async function changeAvatar(avatarId) {
  const response = await fetch(
    `${API_URL}/api/user/${user.Username}/change-avatar/${avatarId}`,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
    }
  );

  const result = await response.json();

  if (response.ok) {
    showResponseMessage("Avatar changed successfully!", "success");

    // Update avatar image on profile page
    const newAvatarImg = document.createElement("img");
    newAvatarImg.src = `${API_URL}/api/user/Assets/Avatars/avatar${avatarId}.png`;
    newAvatarImg.classList.add("avatar");
    newAvatarImg.alt = `Avatar ${avatarId}`;

    newAvatarImg.addEventListener("click", () => {
      purchasedAvatars();
    });

    // Replace avatar with the new one
    avatarContainer.innerHTML = "";
    document.getElementById("avatar_options").remove();
    avatarContainer.appendChild(newAvatarImg);
  } else {
    showResponseMessage(result.Message || "Error changing avatar", "error");
  }
}

document
  .getElementById("delete_account")
  .addEventListener("click", async () => {
    fetch("/api/user/delete-account", {
      method: "DELETE",
      headers: {
        Authorization: `Bearer ${token}`,
      },
    })
      .then((response) => response.json())
      .then((data) => {
        if (data.message) {
          console.log(data);
          showResponseMessage(data.message, "success");

          // Logout user
          localStorage.clear();
          window.location.href = "/";
        } else {
          showResponseMessage("Failed to delete account", "error");
        }
      })
      .catch((error) => {
        console.error("Error:", error);
        showResponseMessage(
          "An error occurred while deleting the account",
          "error"
        );
      });
  });
