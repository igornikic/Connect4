// Function to show the message and hide it after 3 seconds
export default function showResponseMessage(message, type) {
  responseMessage.textContent = message;
  responseMessage.className = `response show ${type}`;

  setTimeout(() => {
    responseMessage.classList.remove("show");
  }, 3000);
}
