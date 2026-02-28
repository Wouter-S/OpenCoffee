variable "TAG" {
  default = "latest"
}

variable "REGISTRY" {
  default = ""
}

variable "IMAGE_NAME" {
  default = "opencoffee"
}

function "full_image" {
  params = [tag]
  result = notequal("", REGISTRY) ? "${REGISTRY}/${IMAGE_NAME}:${tag}" : "${IMAGE_NAME}:${tag}"
}

group "default" {
  targets = ["opencoffee"]
}

target "opencoffee" {
  context    = "."
  dockerfile = "Dockerfile"
  tags = [
    full_image(TAG),
    full_image("latest"),
  ]
  platforms = ["linux/amd64"]
}
