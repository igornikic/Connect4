import showResponseMessage from "./resMessage.js";

const userString = localStorage.getItem("user");
const user = JSON.parse(userString);
const coinsP = document.getElementById("coins");

document.addEventListener("DOMContentLoaded", async () => {
  document.getElementById("menu").href = `/user/${user.Username}`;
  coinsP.textContent = user.Balance;
  // Fetch all avatars
  const response = await fetch(`${API_URL}/api/user/Assets/Avatars`, {
    method: "GET",
  });

  if (response.ok) {
    const avatarArray = await response.json();
    console.log(avatarArray);
    avatarArray.forEach((img) => {
      const itemDiv = document.createElement("div");
      const avatarDiv = document.createElement("div");
      avatarDiv.classList.add("loading");
      const avatarImg = document.createElement("img");
      const avatarP = document.createElement("p");
      const avatarButton = document.createElement("button");
      avatarImg.src = `${API_URL}/api/user${img.url}`;
      avatarImg.alt = "Avatar";
      avatarImg.width = 128;
      avatarDiv.appendChild(avatarImg);
      avatarImg.onload = function () {
        avatarDiv.classList.remove("loading"); // Remove loading spinner
        avatarImg.style.border = "3px solid #6666ff"; // Add border when image is loaded
        avatarDiv.appendChild(avatarImg); // Add image to container
      };
      avatarImg.onerror = function () {
        avatarDiv.classList.remove("loading");
        avatarDiv.textContent = avatarImg.alt; // Show alt text if image fails to load
      };

      avatarP.textContent = img.price;
      avatarButton.textContent = "BUY";
      // Add listener for click on not purchased avatars
      if (!user.PurchasedAvatars.includes(img.id)) {
        avatarButton.addEventListener("click", () => {
          const isSuccess = purchaseAvatar(user.Username, img.id);
          if (isSuccess) {
            avatarButton.disabled = true;
          }
        });
      } else {
        avatarButton.disabled = true;
      }
      itemDiv.classList.add("shop-item");
      itemDiv.appendChild(avatarDiv);
      itemDiv.appendChild(avatarP);
      itemDiv.appendChild(avatarButton);
      document.getElementById("shop").appendChild(itemDiv);
    });
  }

  async function purchaseAvatar(username, avatarId) {
    const response = await fetch(
      `${API_URL}/api/user/${username}/${avatarId}`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Username: username, AvatarId: avatarId }),
      }
    );

    const result = await response.json();
    if (response.ok) {
      showResponseMessage("Purchase successful!", "success");
      // New balance
      coinsP.textContent = result.newBalance;
      return true;
    } else {
      showResponseMessage(result.message, "error");
      return false;
    }
  }
});
